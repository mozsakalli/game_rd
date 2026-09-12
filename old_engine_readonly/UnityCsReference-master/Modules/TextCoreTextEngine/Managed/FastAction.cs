// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;


namespace UnityEngine.TextCore.Text
{
    ///<exclude />
    public class FastAction
    {
        LinkedList<System.Action> delegates = new LinkedList<System.Action>();

        Dictionary<System.Action, LinkedListNode<System.Action>> lookup = new Dictionary<System.Action, LinkedListNode<System.Action>>();

        ///<exclude />
        public void Add(System.Action rhs)
        {
            if (lookup.ContainsKey(rhs)) return;

            lookup[rhs] = delegates.AddLast(rhs);
        }

        ///<exclude />
        public void Remove(System.Action rhs)
        {
            LinkedListNode<System.Action> node;
            if (lookup.TryGetValue(rhs, out node))
            {
                lookup.Remove(rhs);
                delegates.Remove(node);
            }
        }

        ///<exclude />
        public void Call()
        {
            var node = delegates.First;
            while (node != null)
            {
                node.Value();
                node = node.Next;
            }
        }

        internal void Clear()
        {
            delegates.Clear();
            lookup.Clear();
        }
    }

    ///<exclude />
    public class FastAction<A>
    {
        LinkedList<System.Action<A>> delegates = new LinkedList<System.Action<A>>();

        Dictionary<System.Action<A>, LinkedListNode<System.Action<A>>> lookup = new Dictionary<System.Action<A>, LinkedListNode<System.Action<A>>>();

        ///<exclude />
        public void Add(System.Action<A> rhs)
        {
            if (lookup.ContainsKey(rhs)) return;

            lookup[rhs] = delegates.AddLast(rhs);
        }

        ///<exclude />
        public void Remove(System.Action<A> rhs)
        {
            LinkedListNode<System.Action<A>> node;
            if (lookup.TryGetValue(rhs, out node))
            {
                lookup.Remove(rhs);
                delegates.Remove(node);
            }
        }

        ///<exclude />
        public void Call(A a)
        {
            var node = delegates.First;
            while (node != null)
            {
                node.Value(a);
                node = node.Next;
            }
        }

        internal void Clear()
        {
            delegates.Clear();
            lookup.Clear();
        }
    }

    ///<exclude />
    public class FastAction<A, B>
    {
        LinkedList<System.Action<A, B>> delegates = new LinkedList<System.Action<A, B>>();

        Dictionary<System.Action<A, B>, LinkedListNode<System.Action<A, B>>> lookup = new Dictionary<System.Action<A, B>, LinkedListNode<System.Action<A, B>>>();

        ///<exclude />
        public void Add(System.Action<A, B> rhs)
        {
            if (lookup.ContainsKey(rhs)) return;

            lookup[rhs] = delegates.AddLast(rhs);
        }

        ///<exclude />
        public void Remove(System.Action<A, B> rhs)
        {
            LinkedListNode<System.Action<A, B>> node;
            if (lookup.TryGetValue(rhs, out node))
            {
                lookup.Remove(rhs);
                delegates.Remove(node);
            }
        }

        ///<exclude />
        public void Call(A a, B b)
        {
            var node = delegates.First;
            while (node != null)
            {
                node.Value(a, b);
                node = node.Next;
            }
        }

        internal void Clear()
        {
            delegates.Clear();
            lookup.Clear();
        }
    }

    ///<exclude />
    public class FastAction<A, B, C>
    {
        LinkedList<System.Action<A, B, C>> delegates = new LinkedList<System.Action<A, B, C>>();

        Dictionary<System.Action<A, B, C>, LinkedListNode<System.Action<A, B, C>>> lookup = new Dictionary<System.Action<A, B, C>, LinkedListNode<System.Action<A, B, C>>>();

        ///<exclude />
        public void Add(System.Action<A, B, C> rhs)
        {
            if (lookup.ContainsKey(rhs)) return;

            lookup[rhs] = delegates.AddLast(rhs);
        }

        ///<exclude />
        public void Remove(System.Action<A, B, C> rhs)
        {
            LinkedListNode<System.Action<A, B, C>> node;
            if (lookup.TryGetValue(rhs, out node))
            {
                lookup.Remove(rhs);
                delegates.Remove(node);
            }
        }

        ///<exclude />
        public void Call(A a, B b, C c)
        {
            var node = delegates.First;
            while (node != null)
            {
                node.Value(a, b, c);
                node = node.Next;
            }
        }

        internal void Clear()
        {
            delegates.Clear();
            lookup.Clear();
        }
    }
}
