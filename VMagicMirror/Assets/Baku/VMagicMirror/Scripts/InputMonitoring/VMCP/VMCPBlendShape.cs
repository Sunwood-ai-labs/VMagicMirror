using System.Collections.Generic;
using UniRx;
using UnityEngine;
using UniVRM10;
using Zenject;

namespace Baku.VMagicMirror.VMCP
{
    /// <summary>
    /// ロードされたVRMに定義されているキー値を、VMCPで受信した値ベースで受け渡しするクラス。
    /// </summary>
    public class VMCPBlendShape : PresenterBase, ITickable
    {
        private const float DisconnectCount = VMCPReceiver.DisconnectCount;

        private static readonly HashSet<ExpressionKey> LipSyncKeys = new HashSet<ExpressionKey>()
        {
            ExpressionKey.Aa,
            ExpressionKey.Ee,
            ExpressionKey.Ih,
            ExpressionKey.Oh,
            ExpressionKey.Ou,
        };

        private readonly List<ExpressionKey> _keys = new List<ExpressionKey>();
        private readonly Dictionary<ExpressionKey, float> _internalValues = new Dictionary<ExpressionKey, float>();
        private readonly Dictionary<ExpressionKey, float> _values = new Dictionary<ExpressionKey, float>();
        //送信側がVRM0.xのケースと1.0のケースで都合が変わるんですよ信じられますか
        private readonly Dictionary<string, ExpressionKey> _stringToKeyCache = new Dictionary<string, ExpressionKey>();

        private readonly ReactiveProperty<bool> _isActive = new ReactiveProperty<bool>(false);
        public IReadOnlyReactiveProperty<bool> IsActive => _isActive;

        private bool _hasModel;
        private readonly IVRMLoadable _vrmLoadable;

        private bool _isConnected;
        private float _disconnectCountDown;

        [Inject]
        public VMCPBlendShape(IVRMLoadable vrmLoadable)
        {
            _vrmLoadable = vrmLoadable;
        }

        public override void Initialize()
        {
            _vrmLoadable.VrmLoaded += OnVrmLoaded;
            _vrmLoadable.VrmDisposing += OnVrmUnloaded;
        }

        void ITickable.Tick()
        {
            if (!_isActive.Value || !_isConnected)
            {
                return;
            }

            //接続しているはずなのにBlendShapeを一定時間受け取れてない場合、リセットする
            if (_disconnectCountDown > 0f)
            {
                _disconnectCountDown -= Time.deltaTime;
                if (_disconnectCountDown <= 0f)
                {
                    _isConnected = false;
                    ResetValues();   
                }
            }
        }

        public void SetActive(bool active)
        {
            if (_isActive.Value && !active)
            {
                _isConnected = false;
                _disconnectCountDown = 0f;
                ResetValues();
            }
            _isActive.Value = active;
        }

        public void SetValue(string rawKey, float value)
        {
            if (!_stringToKeyCache.TryGetValue(rawKey, out var key))
            {
                return;
            }

            _internalValues[key] = Mathf.Clamp01(value);
            _isConnected = true;
            _disconnectCountDown = DisconnectCount;
        }

        private void ResetValues()
        {
            foreach (var key in _keys)
            {
                _internalValues[key] = 0f;
                _values[key] = 0f;
            }
        }
        
        public void Apply()
        {
            //NOTE: Applyでは公開値が切り替わるだけであり、このクラスがVRMの表情を書き換えていいわけではない
            foreach (var pair in _internalValues)
            {
                _values[pair.Key] = pair.Value;
            }
        }

        public void AccumulateLipSyncBlendShape(ExpressionAccumulator accumulator)
        {
            if (!_hasModel)
            {
                return;
            }
            
            foreach (var key in LipSyncKeys)
            {
                accumulator.Accumulate(key, _values[key]);
            }
        }
        
        public void AccumulateAllBlendShape(ExpressionAccumulator accumulator, float mouthWeight = 1f, float nonMouthWeight = 1f)
        {
            if (!_hasModel)
            {
                return;
            }

            foreach (var pair in _values)
            {
                var weight = LipSyncKeys.Contains(pair.Key) ? mouthWeight : nonMouthWeight;
                accumulator.Accumulate(pair.Key, pair.Value * weight);
            }
        }
        
        private void OnVrmLoaded(VrmLoadedInfo info)
        {
            _keys.Clear();
            _values.Clear();
            _internalValues.Clear();
            var keys = info.RuntimeFacialExpression.ExpressionKeys;
            _keys.AddRange(keys);
            foreach (var key in keys)
            {
                _values[key] = 0f;
                _internalValues[key] = 0f;
                _stringToKeyCache[key.ToString()] = key;
            }

            // ここでVRM0用のキーも入れておくことで、VRM0.xのSender Appから「A」が来たとき「Aa」に紐付ける…みたいな措置が取れる。
            // * Senderが VRM1.0未対応なケースをケアしないで良くなったら消したいが、2023-24年くらいでは絶対そうならないと思う
            var vrm0KeyMap = Vrm0BlendShapeKeyUtils.CreateVrm0StringKeyToVrm1KeyMap();
            foreach (var pair in vrm0KeyMap)
            {
                //「同じToString()の結果が得られるけどVRM0とVRM1で指すキーが異なるもの」はVRM1が優先する。そんなキーは無いはずだが。
                if (!_stringToKeyCache.ContainsKey(pair.Key))
                {
                    _stringToKeyCache[pair.Key] = pair.Value;
                }
            }

            _hasModel = true;
        }

        private void OnVrmUnloaded()
        {
            _hasModel = false;
            _keys.Clear();
            _values.Clear();
            _internalValues.Clear();
            _stringToKeyCache.Clear();
        }
    }
}
