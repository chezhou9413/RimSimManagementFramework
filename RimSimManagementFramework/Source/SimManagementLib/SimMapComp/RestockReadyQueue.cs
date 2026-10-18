using System.Collections.Generic;

namespace SimManagementLib.SimMapComp
{
    //补货就绪队列，职责是用链表与节点索引提供请求的常数时间入队、移除和取出。
    internal sealed class RestockReadyQueue
    {
        //就绪队列节点，职责是保存请求键和双向链接并允许对象池复用。
        private sealed class Node
        {
            public RestockTaskKey Key;
            public Node Previous;
            public Node Next;
        }

        private readonly Dictionary<RestockTaskKey, Node> nodes = new Dictionary<RestockTaskKey, Node>();
        private readonly Stack<Node> nodePool = new Stack<Node>();
        private Node first;
        private Node last;

        public int Count => nodes.Count;

        //把尚未就绪的请求追加到队尾，并保证同一请求最多只有一个节点。
        public bool Add(RestockTaskKey key)
        {
            if (nodes.ContainsKey(key))
                return false;
            Node node = nodePool.Count > 0 ? nodePool.Pop() : new Node();
            node.Key = key;
            node.Previous = last;
            node.Next = null;
            if (last == null)
                first = node;
            else
                last.Next = node;
            last = node;
            nodes.Add(key, node);
            return true;
        }

        //从队首取出一个请求，同时删除其节点索引。
        public bool TryTake(out RestockTaskKey key)
        {
            Node node = first;
            if (node == null)
            {
                key = default(RestockTaskKey);
                return false;
            }
            key = node.Key;
            DetachAndRecycle(node);
            return true;
        }

        //按请求键立即移除就绪节点，避免队列积累需要线性跳过的陈旧项。
        public bool Remove(RestockTaskKey key)
        {
            if (!nodes.TryGetValue(key, out Node node))
                return false;
            DetachAndRecycle(node);
            return true;
        }

        //断开活动节点并放回对象池，避免周期性重试持续分配链表节点。
        private void DetachAndRecycle(Node node)
        {
            nodes.Remove(node.Key);
            if (node.Previous == null)
                first = node.Next;
            else
                node.Previous.Next = node.Next;
            if (node.Next == null)
                last = node.Previous;
            else
                node.Next.Previous = node.Previous;
            node.Previous = null;
            node.Next = null;
            nodePool.Push(node);
        }

        //清空就绪顺序和节点索引。
        public void Clear()
        {
            Node node = first;
            while (node != null)
            {
                Node next = node.Next;
                node.Previous = null;
                node.Next = null;
                nodePool.Push(node);
                node = next;
            }
            first = null;
            last = null;
            nodes.Clear();
        }
    }
}
