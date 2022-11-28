using System.Collections.Generic;
using UniVRM10;
using Zenject;

namespace Baku.VMagicMirror
{
    /// <summary>
    /// 表情のBlendShapeについて「Accumulate -> Apply」というVRM 0.x用の操作をサポートするための、
    /// VRM10のRuntime.Expressionのラッパー
    /// </summary>
    public class ExpressionAccumulator : IInitializable
    {
        private readonly Dictionary<ExpressionKey, float> _values = new Dictionary<ExpressionKey, float>();
        private readonly HashSet<ExpressionKey> _keys = new HashSet<ExpressionKey>();
        private readonly IVRMLoadable _vrmLoadable;

        private bool _hasModel;
        private Vrm10RuntimeExpression _expression;

        public ExpressionAccumulator(IVRMLoadable vrmLoadable)
        {
            _vrmLoadable = vrmLoadable;
        }

        public void Initialize()
        {
            _vrmLoadable.VrmLoaded += info =>
            {
                _expression = info.instance.Runtime.Expression;
                var exprMap = info.instance.Vrm.Expression.LoadExpressionMap();

                _values.Clear();
                _keys.Clear();
                foreach (var key in exprMap.Keys)
                {
                    _values[key] = 0f;
                    _keys.Add(key);
                }
                _hasModel = true;
            };

            _vrmLoadable.VrmDisposing += () =>
            {
                _hasModel = false;
                _expression = null;
            };
        }

        public void Accumulate(ExpressionKey key, float value)
        {
            if (_hasModel && _keys.Contains(key))
            {
                _values[key] += value;
            }
        }

        public void Apply() => _expression?.SetWeights(_values);

        public void ResetValues()
        {
            foreach (var k in _keys)
            {
                _values[k] = 0f;
            }
        }

        public float GetValue(ExpressionKey key) => _keys.Contains(key) ? _values[key] : 0f;

        public void SetZero(ExpressionKey key)
        {
            if (_keys.Contains(key))
            {
                _values[key] = 0f;
            }
        }

        public void SetZero(IEnumerable<ExpressionKey> keys)
        {
            foreach (var k in keys)
            {
                if (_keys.Contains(k))
                {
                    _values[k] = 0f;
                }
            }
        }
    }
}
