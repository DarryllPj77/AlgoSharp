using AlgoSharp.Core.Engine;
using static AlgoSharp.Core.Engine.CellState;

namespace AlgoSharp.Core.Algorithms;

/// <summary>LIFO stack backed by an array and a <c>top</c> index.</summary>
public sealed class ArrayStack
{
    private readonly int[] items;
    private int top = -1;

    public ArrayStack(int capacity, IEnumerable<int> values)
    {
        items = new int[capacity];
        foreach (int v in values.Take(capacity)) items[++top] = v;
        T = new ArrayTracer(ArrayLayout.Stack, capacity, items[..(top + 1)]);
    }

    public ArrayTracer T { get; }
    public int Count => top + 1;
    public int Capacity => items.Length;
    public int[] ToArray() => items[..(top + 1)];

    // <code id="stack-push">
    public void Push(int value)
    {
        T.At("full").Ptr("top", top).V("top", top).V("capacity", items.Length).Say($"Is there room above top = {top}? Capacity is {items.Length}.");
        if (top == items.Length - 1) // #full
        {
            T.At("throw").Ptr("top", top).V("top", top).Error("Stack overflow: every slot is used.");
            throw new InvalidOperationException("Stack overflow"); // #throw
        }
        top++; // #inc
        T.At("inc").Ptr("top", top).V("top", top).V("value", value).Say($"Move top up to index {top}.");
        items[top] = value; // #write
        T.Create(top, value);
        T.At("write").Ptr("top", top).V("top", top).V("items[top]", items[top]).Say($"Place {value} on top. Push is O(1): nothing else moves.");
    }
    // </code>

    // <code id="stack-pop">
    public int Pop()
    {
        T.At("empty").Ptr("top", top).V("top", top).Say(top == -1 ? "top = -1 means the stack is empty." : $"top = {top}, so the stack is not empty.");
        if (top == -1) // #empty
        {
            T.At("throw").V("top", top).Error("Stack underflow: there is nothing to pop.");
            throw new InvalidOperationException("Stack underflow"); // #throw
        }
        int value = items[top]; // #read
        T.At("read").Hl(Key, top).Ptr("top", top).V("top", top).V("value", value).Say($"Read the top value: {value}.");
        T.Remove(top);
        top--; // #dec
        T.At("dec").Ptr("top", top).V("top", top).V("value", value).Say($"Lower top to {top}. The slot is now free (its old bits are simply ignored).");
        return value; // #ret
    }
    // </code>

    // <code id="stack-peek">
    public int Peek()
    {
        if (top == -1) // #empty
        {
            T.At("throw").V("top", top).Error("The stack is empty: there is nothing to peek at.");
            throw new InvalidOperationException("Stack is empty"); // #throw
        }
        T.Mark(Found, top);
        T.At("ret").Ptr("top", top).V("top", top).V("items[top]", items[top]).Say($"Peek returns {items[top]} without removing it.");
        return items[top]; // #ret
    }
    // </code>
}

/// <summary>FIFO queue backed by a circular array.</summary>
public sealed class ArrayQueue
{
    private readonly int[] items;
    private int front;
    private int rear;
    private int count;

    public ArrayQueue(int capacity, IEnumerable<int> values)
    {
        items = new int[capacity];
        foreach (int v in values.Take(capacity))
        {
            items[rear] = v;
            rear = (rear + 1) % capacity;
            count++;
        }
        T = new ArrayTracer(ArrayLayout.Cells, capacity, items[..count]);
    }

    public ArrayTracer T { get; }
    public int Count => count;
    public int Capacity => items.Length;
    public int Front => front;
    public int Rear => rear;

    public int[] ToArray()
    {
        var result = new int[count];
        for (int i = 0; i < count; i++) result[i] = items[(front + i) % items.Length];
        return result;
    }

    // <code id="queue-enqueue">
    public void Enqueue(int value)
    {
        T.At("full").Ptr("front", front).Ptr("rear", rear).V("count", count).V("capacity", items.Length).Say($"Is there room? count = {count}, capacity = {items.Length}.");
        if (count == items.Length) // #full
        {
            T.At("throw").Ptr("front", front).Ptr("rear", rear).Error("The queue is full.");
            throw new InvalidOperationException("Queue is full"); // #throw
        }
        items[rear] = value; // #write
        T.Create(rear, value);
        T.At("write").Ptr("front", front).Ptr("rear", rear).V("rear", rear).V("value", value).Say($"Write {value} into the free slot at rear = {rear}.");
        rear = (rear + 1) % items.Length; // #rear
        T.At("rear").Ptr("front", front).Ptr("rear", rear).V("rear", rear).Say(rear == 0 ? "Advance rear. It wraps around to 0 — that is what makes the array circular." : $"Advance rear to {rear}.");
        count++; // #count
        T.At("count").Ptr("front", front).Ptr("rear", rear).V("count", count).Say($"count = {count}. Enqueue is O(1).");
    }
    // </code>

    // <code id="queue-dequeue">
    public int Dequeue()
    {
        if (count == 0) // #empty
        {
            T.At("throw").Ptr("front", front).Ptr("rear", rear).Error("The queue is empty: there is nothing to dequeue.");
            throw new InvalidOperationException("Queue is empty"); // #throw
        }
        int value = items[front]; // #read
        T.At("read").Hl(Key, front).Ptr("front", front).Ptr("rear", rear).V("front", front).V("value", value).Say($"The oldest value sits at front = {front}: {value}.");
        T.Remove(front);
        front = (front + 1) % items.Length; // #front
        T.At("front").Ptr("front", front).Ptr("rear", rear).V("front", front).V("value", value).Say(front == 0 ? "Advance front; it wraps around to 0." : $"Advance front to {front}. No elements are shifted.");
        count--; // #count
        T.At("count").Ptr("front", front).Ptr("rear", rear).V("count", count).Say($"count = {count}. Return {value}. Dequeue is O(1).");
        return value; // #ret
    }
    // </code>

    // <code id="queue-peek">
    public int Peek()
    {
        if (count == 0) // #empty
        {
            T.At("throw").Error("The queue is empty: there is nothing to peek at.");
            throw new InvalidOperationException("Queue is empty"); // #throw
        }
        T.Mark(Found, front);
        T.At("ret").Ptr("front", front).Ptr("rear", rear).V("front", front).V("items[front]", items[front]).Say($"Peek returns the front value {items[front]} without removing it.");
        return items[front]; // #ret
    }
    // </code>
}
