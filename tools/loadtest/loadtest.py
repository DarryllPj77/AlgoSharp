#!/usr/bin/env python3
"""
AlgoSharp compile load test (standard library only).

Drives the server's real compile gate and Roslyn pipeline through the load-test endpoints, which exist only when
Diagnostics:LoadTestEndpoints=true (never in production). Run it against a LOCAL container with Render Free limits:

  docker run -d --name algosharp-load -p 127.0.0.1:5330:8080 --memory 512m --cpus 0.1 \
      -e Diagnostics__LoadTestEndpoints=true -e Sandbox__Mode=Browser -e HttpsRedirection=false \
      -e ReverseProxy__Enabled=false algosharp-web:load
  python tools/loadtest/loadtest.py --base http://127.0.0.1:5330 --container algosharp-load --users 5 10 20

Each virtual user behaves like a beginner in Solve It: edits code (a live check every 1.5-3 s, the editor's
debounce), and roughly every sixth action presses Run (a full compile + instrumentation; execution would
happen in their browser). Per level: a burst (everyone presses Run at the same moment), then a sustained phase.
"""
import argparse, json, random, statistics, subprocess, threading, time, urllib.error, urllib.request

SOLUTIONS = {
    "two-sum": """public class Solution {
    public int[] TwoSum(int[] nums, int target) {
        var seenAt = new Dictionary<int, int>();
        for (int idx = 0; idx < nums.Length; idx++) {
            int need = target - nums[idx];
            if (seenAt.ContainsKey(need)) return new int[] { seenAt[need], idx };
            seenAt[nums[idx]] = idx;
        }
        return new int[0];
    }
}""",
    "contains-duplicate": """public class Solution {
    public bool ContainsDuplicate(int[] nums) {
        var seen = new HashSet<int>();
        foreach (int x in nums) { if (!seen.Add(x)) return true; }
        return false;
    }
}""",
    "best-time-to-buy-and-sell-stock": """public class Solution {
    public int MaxProfit(int[] prices) {
        int min = int.MaxValue, best = 0;
        for (int i = 0; i < prices.Length; i++) {
            if (prices[i] < min) min = prices[i];
            else if (prices[i] - min > best) best = prices[i] - min;
        }
        return best;
    }
}""",
}


def post(base, body, timeout=60):
    data = json.dumps(body).encode()
    req = urllib.request.Request(base + "/diag/compile", data=data, headers={"Content-Type": "application/json"})
    t0 = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            payload = json.loads(r.read() or b"{}")
            return r.status, payload.get("status"), (time.perf_counter() - t0) * 1000
    except urllib.error.HTTPError as e:
        try:
            payload = json.loads(e.read() or b"{}")
        except ValueError:
            payload = {}
        return e.code, payload.get("status"), (time.perf_counter() - t0) * 1000
    except Exception as e:  # connection refused/reset, timeout: count as an error
        return 0, type(e).__name__, (time.perf_counter() - t0) * 1000


def variant(code, n):
    # Each edit changes the code a little, like typing does (no two requests are identical).
    return code.replace("public class Solution {", f"public class Solution {{\n    // edit {n}", 1)


class Sampler(threading.Thread):
    """Container memory / CPU every second via `docker stats`."""

    def __init__(self, container):
        super().__init__(daemon=True)
        self.container, self.mem, self.cpu, self.running = container, [], [], True

    def run(self):
        while self.running:
            try:
                out = subprocess.run(["docker", "stats", "--no-stream", "--format", "{{.MemUsage}}|{{.CPUPerc}}", self.container],
                                     capture_output=True, text=True, timeout=10).stdout.strip()
                mem, cpu = out.split("|")
                used = mem.split("/")[0].strip()
                mb = float(used[:-3]) * (1024 if used.endswith("GiB") else 1) if used[-3:] in ("MiB", "GiB") else float(used[:-2]) / 1024
                self.mem.append(mb)
                self.cpu.append(float(cpu.strip().rstrip("%")))
            except Exception:
                pass
            time.sleep(1)


def summarize(name, results):
    by_kind = {}
    for kind, code, status, ms in results:
        by_kind.setdefault(kind, []).append((code, status, ms))
    lines = []
    for kind, rows in sorted(by_kind.items()):
        ok = [ms for c, s, ms in rows if c == 200 and s == "Ok"]
        sup = sum(1 for c, s, _ in rows if s == "Superseded")
        r429 = sum(1 for c, _, _ in rows if c == 429)
        r503 = sum(1 for c, _, _ in rows if c == 503)
        err = sum(1 for c, _, _ in rows if c not in (200, 429, 503))
        pct = lambda p: round(sorted(ok)[min(len(ok) - 1, int(len(ok) * p))]) if ok else None
        lines.append(f"  {name:<10} {kind:<5} n={len(rows):<4} ok={len(ok):<4} superseded={sup:<3} 429={r429:<3} 503={r503:<3} errors={err:<3} "
                     f"p50={pct(.5)} ms p95={pct(.95)} ms max={round(max(ok)) if ok else None} ms")
    return lines


def run_level(base, users, seconds, sampler):
    results, lock = [], threading.Lock()

    def record(kind, r):
        with lock:
            results.append((kind,) + r)

    # Burst: everyone presses Run at the same instant.
    burst, barrier = [], threading.Barrier(users)

    def burst_user(u):
        slug = list(SOLUTIONS)[u % len(SOLUTIONS)]
        barrier.wait()
        r = post(base, {"problem": slug, "code": variant(SOLUTIONS[slug], u), "user": f"burst{users}-{u}", "kind": "run"})
        with lock:
            burst.append(("run",) + r)

    threads = [threading.Thread(target=burst_user, args=(u,)) for u in range(users)]
    for t in threads: t.start()
    for t in threads: t.join()
    time.sleep(3)

    # Sustained: realistic editing with occasional runs.
    mem_start, stop = len(sampler.mem), time.time() + seconds

    def user(u):
        rng = random.Random(u)
        slug = list(SOLUTIONS)[u % len(SOLUTIONS)]
        n = 0
        time.sleep(rng.uniform(0, 2))
        while time.time() < stop:
            n += 1
            kind = "run" if n % 6 == 0 else "check"
            record(kind, post(base, {"problem": slug, "code": variant(SOLUTIONS[slug], n), "user": f"u{users}-{u}", "kind": kind}))
            time.sleep(rng.uniform(1.5, 3.0))

    threads = [threading.Thread(target=user, args=(u,)) for u in range(users)]
    for t in threads: t.start()
    for t in threads: t.join()
    mem = sampler.mem[mem_start:] or [0]
    cpu = sampler.cpu[mem_start:] or [0]
    return burst, results, max(mem), statistics.mean(cpu), max(cpu)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="http://127.0.0.1:5330")
    ap.add_argument("--container", default="algosharp-load")
    ap.add_argument("--users", type=int, nargs="+", default=[5, 10, 20])
    ap.add_argument("--seconds", type=int, default=60)
    args = ap.parse_args()

    for _ in range(3):  # warm-up: JIT and Roslyn's first compilation are slow; don't count them
        post(args.base, {"problem": "two-sum", "code": SOLUTIONS["two-sum"], "user": "warmup", "kind": "check"})
    sampler = Sampler(args.container)
    sampler.start()
    time.sleep(2)
    print(f"idle memory: {sampler.mem[-1] if sampler.mem else '?'} MiB")
    for users in args.users:
        burst, results, peak_mem, cpu_avg, cpu_max = run_level(args.base, users, args.seconds, sampler)
        print(f"\n=== {users} simultaneous users ===")
        print("\n".join(summarize("burst", burst)))
        print("\n".join(summarize("sustained", results)))
        print(f"  peak memory {peak_mem:.0f} MiB, CPU avg {cpu_avg:.1f}% max {cpu_max:.1f}% (of one core; the container is capped)")
        try:
            with urllib.request.urlopen(args.base + "/diag/stats", timeout=10) as r:
                print("  server:", r.read().decode())
        except Exception as e:
            print("  server stats unavailable:", e)
        time.sleep(5)
    sampler.running = False


if __name__ == "__main__":
    main()
