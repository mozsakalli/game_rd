using System;
using System.Collections.Generic;

namespace Digiplay
{
    public class Map<TKey, TValue> : Dictionary<TKey, TValue> //where TValue : class
    {
        bool IsNull(TKey val)
        {
            return !(val is ValueType) && null == (object)val;
        }

        bool IsNull(TValue val)
        {
            return !(val is ValueType) && null == (object)val;
        }

        public new TValue this[TKey key]
        {
            get
            {
                if (IsNull(key))
                    return default(TValue);
                TValue result;
                if (base.TryGetValue(key, out result))
                    return result;
                return default(TValue);
            }
            set
            {
                if (!IsNull(key))
                {
                    if (IsNull(value))
                    {
                        if (ContainsKey(key))
                            Remove(key);
                    }
                    else base[key] = value;
                    /*
                    if (!ContainsKey(key))
                        Add(key, value);
                    else
                        base[key] = value;
                    */
                }
            }
        }

        public Dynamic ToDynamic(TKey key)
        {
            return this[key] as Dynamic;
        }

        public int ToInt(TKey key)
        {
            return this[key].ToInt();
        }

        public float ToFloat(TKey key)
        {
            return this[key].ToFloat();
        }
    }

    public class Dynamic : Map<string, object>
    {
        public Dynamic AddAll(Dynamic source)
        {
            var iter = source.GetEnumerator();
            while (iter.MoveNext())
            {
                this[iter.Current.Key] = iter.Current.Value;
            }
            return this;
        }

        public Dynamic Clone()
        {
            return new Dynamic().AddAll(this);
        }
    }
}