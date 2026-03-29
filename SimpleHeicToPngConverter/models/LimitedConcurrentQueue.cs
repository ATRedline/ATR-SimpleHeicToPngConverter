using System.Collections.Concurrent;

namespace SimpleHeicToPngConverter.models
{
    public class LimitedConcurrentQueue<T> : ConcurrentQueue<T>
    {
        private int _size = 0;

        private LimitedConcurrentQueue()
        {
        }

        public LimitedConcurrentQueue(int size)
        {
            _size = size;
        }

        public void Add(T item)
        {
            if (Count >= _size)
            {
                var itemsToRemove = (Count - _size) + 1;

                for (int i = 0; i < itemsToRemove; i++)
                {
                    TryDequeue(out T? _);
                }
            }

            Enqueue(item);
        }
    }
}
