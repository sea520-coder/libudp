#if DOTNET_CORE
using System.Buffers;
#endif
using System;
using System.Collections.Generic;
using System.Threading;

namespace LowLevelTransport.Utils
{
    public static class MemoryPool
    {
        public static readonly int MaxSize = 1 << 20;
        private static long totalUsed = 0;
#if DOTNET_CORE
        private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;
#else 
        private static readonly BytePool Pool = BytePool.Shared;
#endif
        public static byte[] Malloc(int length)
        {
            Interlocked.Add(ref totalUsed, 1);
            return Pool.Rent(length);
        }

        public static void Free(byte[] array)
        {
            Interlocked.Add(ref totalUsed, -1);
            Pool.Return(array);
        }
    }

    public class BytePool
    {
        private static BytePool shared;
        public static BytePool Shared
        {
            get
            {
                if (shared == null)
                {
                    shared = new BytePool();
                }
                return shared;
            }
        }

        private static readonly ushort MaxSlot = 20;

        public BytePool()
        {
            for (int i = 0; i <= MaxSlot; i++)
            {
                Queue<byte[]> queue = new Queue<byte[]>();
                pool.Add(i, queue);
            }
        }

        private readonly Dictionary<int, Queue<byte[]>> pool = new Dictionary<int, Queue<byte[]>>();

        public byte[] Rent(int minLength)
        {
            if (minLength > ( 1 << MaxSlot ))
            {
                throw new Exception("Rent Byte pool two large!");
            }
            int slot = calcuSlot(minLength);

            lock (pool)
            {
                if (pool[slot].Count == 0)
                {
                    pool[slot].Enqueue(new byte[1 << slot]);
                }
                return pool[slot].Dequeue();
            }
        }

        public void Return(byte[] array)
        {
            int slot = calcuSlot(array.Length);
            if (slot > MaxSlot || array.Length != 1 << slot)
            {
                throw new Exception("return a byte[] not belong to BytePool");
            }

            lock (pool)
            {
                pool[slot].Enqueue(array);
            }
        }

        private int calcuSlot(int length)
        {
            if (length == 0)
            {
                throw new Exception("byte length == 0");
            }

            int i = 0;
            while (true)
            {
                if (( length >> i ) == 0)
                {
                    break;
                }
                i++;
            }

            if (length == ( 1 << ( i - 1 ) ))
            {
                i--;
            }
            return i;
        }
    }
}
