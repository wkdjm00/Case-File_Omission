using System;

namespace CaseFileOmission.Contracts
{
    [Serializable]
    public struct WeightedEntry<T>
    {
        public T Value;
        public float Weight;

        public WeightedEntry(T value, float weight)
        {
            Value = value;
            Weight = weight;
        }
    }
}
