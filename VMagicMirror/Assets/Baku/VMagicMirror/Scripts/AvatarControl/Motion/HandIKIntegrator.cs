﻿using Baku.VMagicMirror.IK;
using UniRx;
using UnityEngine;
using Zenject;

namespace Baku.VMagicMirror
{
    /// <summary>
    /// ユーザーの入力と設定に基づいて、実際にIKを適用していくやつ
    /// </summary>
    public class HandIKIntegrator : MonoBehaviour
    {
        #region settings
        
        //NOTE: ステートパターンがめんどくさいときのステートマシンの実装です。まあステート数少ないので…

        /// <summary> IK種類が変わるときのブレンディングに使う時間。IK自体の無効化/有効化もこの時間で行う </summary>
        private const float HandIkToggleDuration = 0.25f;

        private const float HandIkTypeChangeCoolDown = 0.3f;

        //この時間だけ入力がなかったらマウスやキーボードの操作をしている手を下ろしてもいいよ、という秒数
        //たぶん無いと思うけど、何かの周期とピッタリ合うと嫌なのでてきとーに小数値を載せてます
        public const float AutoHandDownDuration = 10.5f;

        //NOTE: 2秒かけて下ろし、0.4秒で戻す、という速度。戻すほうがスピーディなことに注意
        public const float HandDownBlendSpeed = 1f / 2f;
        public const float HandUpBlendSpeed = 1f / 0.4f;

        [SerializeField] private TypingHandIKGenerator typing = null;
        public TypingHandIKGenerator Typing => typing;

        [SerializeField] private GamepadFingerController gamepadFinger = null;
        [SerializeField] private ArcadeStickFingerController arcadeStickFinger = null;

        [SerializeField] private WaitingBodyMotion waitingBody = null;

        [SerializeField] private FingerController fingerController = null;

        [SerializeField] private GamepadHandIKGenerator.GamepadHandIkGeneratorSetting gamepadSetting = default;

        [SerializeField]
        private ImageBaseHandIkGenerator.ImageBaseHandIkGeneratorSetting imageBaseHandSetting = default;

        
        public MouseMoveHandIKGenerator MouseMove { get; private set; }
        public GamepadHandIKGenerator GamepadHand { get; private set; }
        public MidiHandIkGenerator MidiHand { get; private set; }
        public PresentationHandIKGenerator Presentation { get; private set; }

        private ArcadeStickHandIKGenerator _arcadeStickHand;
        private ImageBaseHandIkGenerator _imageBaseHand;
        private AlwaysDownHandIkGenerator _downHand;
        private PenTabletHandIKGenerator _penTablet;
        

        private Transform _rightHandTarget = null;
        private Transform _leftHandTarget = null;

        private float _leftHandStateBlendCount = 0f;
        private float _rightHandStateBlendCount = 0f;

        private float _leftHandIkChangeCoolDown = 0f;
        private float _rightHandIkChangeCoolDown = 0f;

        private bool _enableHidArmMotion = true;

        public bool EnableHidArmMotion
        {
            get => _enableHidArmMotion;
            set
            {
                _enableHidArmMotion = value;
                MouseMove.EnableUpdate = value;
                _penTablet.EnableUpdate = value;
            }
        }

        private bool _enableHandDownTimeout = true;

        public bool EnableHandDownTimeout
        {
            get => _enableHandDownTimeout;
            set
            {
                _enableHandDownTimeout = value;
                typing.EnableHandDownTimeout = value;
                MouseMove.EnableHandDownTimeout = value;
            }
        }

        public ReactiveProperty<WordToMotionDeviceAssign> WordToMotionDevice { get; } =
            new ReactiveProperty<WordToMotionDeviceAssign>(WordToMotionDeviceAssign.KeyboardWord);
        
        public bool EnablePresentationMode => _keyboardAndMouseMotionMode.Value == KeyboardAndMouseMotionModes.Presentation;
        
        public void SetKeyboardAndMouseMotionMode(int modeIndex)
        {
            if (modeIndex >= -1 &&
                modeIndex < (int) KeyboardAndMouseMotionModes.Unknown &&
                modeIndex != (int) _keyboardAndMouseMotionMode.Value
                )
            {
                var mode = (KeyboardAndMouseMotionModes) modeIndex;
                //NOTE: オプションを変えた直後は手は動かさず、変更後の入力によって手が動く
                _keyboardAndMouseMotionMode.Value = mode;
                //Noneは歴史的経緯によって扱いが特殊なので注意
                EnableHidArmMotion = mode != KeyboardAndMouseMotionModes.None;
            }
        }

        public void SetGamepadMotionMode(int modeIndex)
        {
            if (modeIndex >= 0 &&
                modeIndex <= (int) GamepadMotionModes.Unknown &&
                modeIndex != (int) _gamepadMotionMode.Value
                //DEBUG: とりあえずアケコンと通常ゲームパッドだけやる
                && (modeIndex == 0 || modeIndex == 1)
                )
            {   
                _gamepadMotionMode.Value = (GamepadMotionModes) modeIndex;
                //NOTE: オプションを変えた直後は手はとりあえず動かさないでおく(変更後の入力によって手が動く)。
                //変えた時点で切り替わるほうが嬉しい、と思ったらそういう挙動に直す
            }
        }

        private readonly ReactiveProperty<GamepadMotionModes> _gamepadMotionMode =
            new ReactiveProperty<GamepadMotionModes>(GamepadMotionModes.Gamepad);

        private readonly ReactiveProperty<KeyboardAndMouseMotionModes> _keyboardAndMouseMotionMode = 
            new ReactiveProperty<KeyboardAndMouseMotionModes>(KeyboardAndMouseMotionModes.KeyboardAndTouchPad);
        
        //NOTE: これはすごく特別なフラグで、これが立ってると手のIKに何か入った場合でも手が下がりっぱなしになる
        public ReactiveProperty<bool> AlwaysHandDown { get; } = new ReactiveProperty<bool>(false);

        public bool IsLeftHandGripGamepad => _leftTargetType.Value == HandTargetType.Gamepad;
        public bool IsRightHandGripGamepad => _rightTargetType.Value == HandTargetType.Gamepad;
        
        public Vector3 RightHandPosition => _rightHandTarget.position;
        public Vector3 LeftHandPosition => _leftHandTarget.position;

        public float YOffsetAlways
        {
            get => Typing.YOffsetAlways;
            set
            {
                Typing.YOffsetAlways = value;
                MouseMove.YOffset = value;
                _penTablet.YOffset = value;
                MidiHand.HandOffsetAlways = value;
            }
        }

        #endregion

        
        private HandIkReactionSources _reactionSources;
        private readonly HandIkInputEvents _inputEvents = new HandIkInputEvents();
        
        [Inject]
        public void Initialize(
            IVRMLoadable vrmLoadable, 
            IKTargetTransforms ikTargets, 
            Camera cam,
            ParticleStore particleStore,
            KeyboardProvider keyboardProvider,
            GamepadProvider gamepadProvider,
            MidiControllerProvider midiControllerProvider,
            TouchPadProvider touchPadProvider,
            ArcadeStickProvider arcadeStickProvider,
            PenTabletProvider penTabletProvider,
            HandTracker handTracker
            )
        {
            _reactionSources = new HandIkReactionSources(
                particleStore,
                fingerController,
                gamepadFinger,
                arcadeStickFinger
            );
            var runtimeConfig = new HandIkRuntimeConfigs(
                AlwaysHandDown,
                _leftTargetType,
                _rightTargetType,
                _keyboardAndMouseMotionMode,
                _gamepadMotionMode,
                WordToMotionDevice,
                CheckCoolDown,
                CheckTypingOrMouseHandsCanMoveDown
            );
            var dependency = new HandIkGeneratorDependency(
                this, _reactionSources, runtimeConfig, _inputEvents
                );

            _rightHandTarget = ikTargets.RightHand;
            _leftHandTarget = ikTargets.LeftHand;

            vrmLoadable.VrmLoaded += OnVrmLoaded;
            vrmLoadable.VrmDisposing += OnVrmDisposing;        

            MouseMove = new MouseMoveHandIKGenerator(dependency, touchPadProvider);
            MidiHand = new MidiHandIkGenerator(dependency, midiControllerProvider);
            GamepadHand = new GamepadHandIKGenerator(
                dependency, vrmLoadable, waitingBody, gamepadProvider, gamepadSetting
                );
            Presentation = new PresentationHandIKGenerator(dependency, vrmLoadable, cam);
            _arcadeStickHand = new ArcadeStickHandIKGenerator(dependency, vrmLoadable, arcadeStickProvider);
            _imageBaseHand = new ImageBaseHandIkGenerator(dependency, handTracker, imageBaseHandSetting, vrmLoadable);
            _downHand = new AlwaysDownHandIkGenerator(dependency, vrmLoadable);
            _penTablet = new PenTabletHandIKGenerator(dependency, vrmLoadable, penTabletProvider);
            
            typing.SetUp(keyboardProvider, dependency);

            MouseMove.DownHand = _downHand;
            typing.DownHand = _downHand;

            //TODO: TypingだけMonoBehaviourなせいで若干ダサい
            foreach (var generator in new HandIkGeneratorBase[]
                {
                    MouseMove, MidiHand, GamepadHand, _arcadeStickHand, Presentation, _imageBaseHand, _downHand, _penTablet,
                })
            {
                if (generator.LeftHandState != null)
                {
                    generator.LeftHandState.RequestToUse += SetLeftHandState;
                }

                if (generator.RightHandState != null)
                {
                    generator.RightHandState.RequestToUse += SetRightHandState;
                }
            }
            
            Typing.LeftHand.RequestToUse += SetLeftHandState;
            Typing.RightHand.RequestToUse += SetRightHandState;
        }

        //NOTE: prevのStateは初めて手がキーボードから離れるまではnull
        private IHandIkState _prevRightHand = null;
        private IHandIkState _prevLeftHand = null;

        //NOTE: こっちはStart()以降は非null
        private IHandIkState _currentRightHand = null;
        private IHandIkState _currentLeftHand = null;

        //NOTE: 値自体はCurrentRightHand.TargetTypeとかと等しい。値を他のIKに露出するために使う
        private readonly ReactiveProperty<HandTargetType> _leftTargetType 
            = new ReactiveProperty<HandTargetType>(HandTargetType.Keyboard);
        private readonly ReactiveProperty<HandTargetType> _rightTargetType
            = new ReactiveProperty<HandTargetType>(HandTargetType.Keyboard);
        public ReactiveProperty<HandTargetType> RightTargetType => _rightTargetType;

        #region API

        #region Keyboard and Mouse
        
        public void KeyDown(string keyName)
        {
            if (!EnableHidArmMotion)
            {
                return;
            }
            _inputEvents.RaiseKeyDown(keyName);
        }

        public void KeyUp(string keyName)
        {
            if (!EnableHidArmMotion)
            {
                return;
            }
            _inputEvents.RaiseKeyUp(keyName);
        }
        

        public void MoveMouse(Vector3 mousePosition)
        {
            var targetType =
                (_keyboardAndMouseMotionMode.Value == KeyboardAndMouseMotionModes.KeyboardAndTouchPad) ? HandTargetType.Mouse :
                (_keyboardAndMouseMotionMode.Value == KeyboardAndMouseMotionModes.Presentation) ? HandTargetType.Presentation :
                    HandTargetType.PenTablet;

            if (!EnableHidArmMotion || !CheckCoolDown(ReactedHand.Right, targetType))
            {
                return;
            }
            
            _inputEvents.RaiseMoveMouse(mousePosition);
        }

        public void OnMouseButton(string button)
        {
            if (!EnablePresentationMode && EnableHidArmMotion && !AlwaysHandDown.Value)
            {
                _inputEvents.RaiseOnMouseButton(button);
            }
        }

        #endregion
        
        #region Gamepad
        
        //NOTE: ButtonDown/ButtonUpで反応する手が非自明なものはHandIk側でCooldownチェックをしてほしい…が、
        //やらないでも死ぬほどの問題ではない
        public void MoveLeftGamepadStick(Vector2 v)
        {
            if (WordToMotionDevice.Value == WordToMotionDeviceAssign.Gamepad ||
                !CheckCoolDown(ReactedHand.Left, HandTargetType.Gamepad))
            {
                return;
            }
            
            _inputEvents.RaiseMoveLeftGamepadStick(v);
        }

        public void MoveRightGamepadStick(Vector2 v)
        {
            if (WordToMotionDevice.Value == WordToMotionDeviceAssign.Gamepad ||
                !CheckCoolDown(ReactedHand.Right, HandTargetType.Gamepad))
            {
                return;
            }
            
            _inputEvents.RaiseMoveRightGamepadStick(v);
        }

        public void GamepadButtonDown(GamepadKey key)
        {
            if (WordToMotionDevice.Value == WordToMotionDeviceAssign.Gamepad)
            {
                return;
            }

            _inputEvents.RaiseGamepadButtonDown(key);
        }

        public void GamepadButtonUp(GamepadKey key)
        {
            if (WordToMotionDevice.Value == WordToMotionDeviceAssign.Gamepad)
            {
                return;
            }
            
            _inputEvents.RaiseGamepadButtonUp(key);
        }

        public void ButtonStick(Vector2Int pos)
        {
            if (WordToMotionDevice.Value == WordToMotionDeviceAssign.Gamepad || 
                !CheckCoolDown(ReactedHand.Left, HandTargetType.Gamepad))
            {
                return;
            }
            
            _inputEvents.RaiseGamepadButtonStick(pos);
        }
        
        #endregion
        
        #region Midi Controller
        
        public void KnobValueChange(int knobNumber, float value)
        {
            if (WordToMotionDevice.Value != WordToMotionDeviceAssign.MidiController)
            {
                _inputEvents.RaiseKnobValueChange(knobNumber, value);
            }
        }
        
        public void NoteOn(int noteNumber)
        {
            if (WordToMotionDevice.Value != WordToMotionDeviceAssign.MidiController)
            {
                _inputEvents.RaiseNoteOn(noteNumber);
            }
        }

        #endregion
        
        #endregion

        private void Start()
        {
            _currentRightHand = Typing.RightHand;
            _currentLeftHand = Typing.LeftHand;
            _leftHandStateBlendCount = HandIkToggleDuration;
            _rightHandStateBlendCount = HandIkToggleDuration;

            MouseMove.Start();
            Presentation.Start();
            GamepadHand.Start();
            MidiHand.Start();
            _arcadeStickHand.Start();
            _imageBaseHand.Start();
        }
        
        private void OnVrmLoaded(VrmLoadedInfo info)
        {
            fingerController.Initialize(info.animator);
            
            //キャラロード前のHandDownとブレンドするとIK位置が原点に飛ぶので、その値を捨てる
            MouseMove.ResetHandDownTimeout(true);
            Typing.ResetLeftHandDownTimeout(true);
            Typing.ResetRightHandDownTimeout(true);
            
            //NOTE: 初期姿勢は「トラッキングできてない(はずの)画像ベースハンドトラッキングのやつ」にする。
            //こうすると棒立ちになるので都合がよい
            _imageBaseHand.HasRightHandUpdate = false;
            SetRightHandState(_imageBaseHand.RightHandState);
            _imageBaseHand.HasLeftHandUpdate = false;
            SetLeftHandState(_imageBaseHand.LeftHandState);
        }

        private void OnVrmDisposing()
        {
            fingerController.Dispose();
        }
        
        private void Update()
        {
            MouseMove.Update();
            Presentation.Update();
            GamepadHand.Update();
            MidiHand.Update();
            _arcadeStickHand.Update();
            _penTablet.Update();
            _imageBaseHand.Update();

            //画像処理の手検出があったらそっちのIKに乗り換える
            if (_imageBaseHand.HasRightHandUpdate)
            {
                _imageBaseHand.HasRightHandUpdate = false;
                SetRightHandState(_imageBaseHand.RightHandState);
            }

            if (_imageBaseHand.HasLeftHandUpdate)
            {
                _imageBaseHand.HasLeftHandUpdate = false;
                SetLeftHandState(_imageBaseHand.LeftHandState);
            }
            
            //現在のステート + 必要なら直前ステートも参照してIKターゲットの位置、姿勢を更新する
            UpdateLeftHand();
            UpdateRightHand();
        }

        private void LateUpdate()
        {
            MouseMove.LateUpdate();
            GamepadHand.LateUpdate();
            MidiHand.LateUpdate();
            Presentation.LateUpdate();
            _arcadeStickHand.LateUpdate();
            _penTablet.LateUpdate();
            _imageBaseHand.LateUpdate();
        }
        
        private void UpdateLeftHand()
        {
            if (_leftHandIkChangeCoolDown > 0)
            {
                _leftHandIkChangeCoolDown -= Time.deltaTime;
            }
            
            //普通の状態: 複数ステートのブレンドはせず、今のモードをそのまま通す
            if (_leftHandStateBlendCount >= HandIkToggleDuration)
            {
                _leftHandTarget.localPosition = _currentLeftHand.Position;
                _leftHandTarget.localRotation = _currentLeftHand.Rotation;
                return;
            }

            //NOTE: ここの下に来る時点では必ず_prevLeftHandに非null値が入る実装になってます

            _leftHandStateBlendCount += Time.deltaTime;
            //prevStateと混ぜるための比率
            float t = CubicEase(_leftHandStateBlendCount / HandIkToggleDuration);
            _leftHandTarget.localPosition = Vector3.Lerp(
                _prevLeftHand.Position,
                _currentLeftHand.Position,
                t
            );

            _leftHandTarget.localRotation = Quaternion.Slerp(
                _prevLeftHand.Rotation,
                _currentLeftHand.Rotation,
                t
            );
        }

        private void UpdateRightHand()
        {
            if (_rightHandIkChangeCoolDown > 0f)
            {
                _rightHandIkChangeCoolDown -= Time.deltaTime;
            }
            
            //普通の状態: 複数ステートのブレンドはせず、今のモードをそのまま通す
            if (_rightHandStateBlendCount >= HandIkToggleDuration)
            {
                _rightHandTarget.localPosition = _currentRightHand.Position;
                _rightHandTarget.localRotation = _currentRightHand.Rotation;
                return;
            }

            //NOTE: 実装上ここの下に来る時点で_prevRightHandが必ず非nullなのでnullチェックはすっ飛ばす
            
            _rightHandStateBlendCount += Time.deltaTime;
            //prevStateと混ぜるための比率
            float t = CubicEase(_rightHandStateBlendCount / HandIkToggleDuration);
            
            _rightHandTarget.localPosition = Vector3.Lerp(
                _prevRightHand.Position,
                _currentRightHand.Position,
                t
            );

            _rightHandTarget.localRotation = Quaternion.Slerp(
                _prevRightHand.Rotation,
                _currentRightHand.Rotation,
                t
            );
        }
        
        private void SetLeftHandState(IHandIkState state)
        {
            var targetType = state.TargetType;
            
            if (_leftTargetType.Value == targetType || 
                (AlwaysHandDown.Value && targetType != HandTargetType.AlwaysDown))
            {
                //書いてる通りだが、同じ状態には遷移できない + 手下げモードのときは他のモードにならない
                return;
            }


            _leftTargetType.Value = targetType;
            _prevLeftHand = _currentLeftHand;
            _currentLeftHand = state;
            
            _leftHandIkChangeCoolDown = HandIkTypeChangeCoolDown;
            _leftHandStateBlendCount = 0f;

            //Stateの遷移処理。ここで指とかを更新させる
            _prevLeftHand.Quit(_currentLeftHand);
            _currentLeftHand.Enter(_prevLeftHand);
        }

        private void SetRightHandState(IHandIkState state)
        {
            var targetType = state.TargetType;
            
            if (_rightTargetType.Value == targetType || 
                (AlwaysHandDown.Value && targetType != HandTargetType.AlwaysDown))
            {
                //書いてる通りだが、同じ状態には遷移できない + 手下げモードのときは他のモードにならない
                return;
            }

            _rightTargetType.Value = targetType;
            _prevRightHand = _currentRightHand;
            _currentRightHand = state;
            
            _rightHandIkChangeCoolDown = HandIkTypeChangeCoolDown;
            _rightHandStateBlendCount = 0f;

            //Stateの遷移処理。ここで指とかを更新させる
            _prevRightHand.Quit(_currentRightHand);
            _currentRightHand.Enter(_prevRightHand);
        }
        
        // NOTE: クールダウン判定をSetLeft|RightHandState時に行う手もあるが、色々考えて筋悪そうなので却下

        // クールダウンタイムを考慮したうえで、モーションを適用してよいかどうかを確認する
        private bool CheckCoolDown(ReactedHand hand, HandTargetType targetType)
        {
            if ((hand == ReactedHand.Left && targetType == _leftTargetType.Value) ||
                (hand == ReactedHand.Right && targetType == _rightTargetType.Value))
            {
                //同じデバイスを続けて触っている -> 素通しでOK
                return true;
            }

            return
                (hand == ReactedHand.Left && _leftHandIkChangeCoolDown <= 0) ||
                (hand == ReactedHand.Right && _rightHandIkChangeCoolDown <= 0);
        }
        
        // マウス/タイピングIKに関して、タイムアウトによって腕を下げていいかどうかを取得します。
        private bool CheckTypingOrMouseHandsCanMoveDown()
        {
            var left = _leftTargetType.Value;
            var right = _rightTargetType.Value;
            
            if (left != HandTargetType.Keyboard &&
                right != HandTargetType.Keyboard &&
                right != HandTargetType.Mouse)
            {
                //この場合は特に意味がない
                return false;
            }

            //NOTE: ペンタブモードの場合、ペンを持った右手+左手(キーボード上であることが多い)のいずれも降ろさせない。
            if (right == HandTargetType.PenTablet)
            {
                return false;
            }

            bool leftHandIsReady = left != HandTargetType.Keyboard || typing.LeftHandTimeOutReached;

            bool rightHandIsReady =
                (right == HandTargetType.Keyboard && typing.RightHandTimeOutReached) ||
                (right == HandTargetType.Mouse && MouseMove.IsNoInputTimeOutReached) ||
                (right != HandTargetType.Keyboard && right != HandTargetType.Mouse);

            return leftHandIsReady && rightHandIsReady;
        }

        // x in [0, 1] を y in [0, 1]へ3次補間する
        private static float CubicEase(float rate) 
            => 2 * rate * rate * (1.5f - rate);
    }

    /// <summary>
    /// 手のIKの一覧。常時手下げモードがあったり、片方の腕にしか適合しない値が入ってたりすることに注意
    /// </summary>
    public enum HandTargetType
    {
        // NOTE: 右手にのみ使う
        Mouse,
        Keyboard,
        // NOTE: 右手にのみ使う。「プレゼンモードの場合の左手」とそうでない左手はどちらもKeyboardで統一的に扱う
        Presentation,
        // NOTE: 両手に
        PenTablet,
        Gamepad,
        ArcadeStick,
        MidiController,
        ImageBaseHand,
        AlwaysDown,
        Unknown,
    }
    
    //TODO: ここに書くのは変なので単独のスクリプト作った方が良いかもしれない。が、当面は放置でもいいかな…
    public enum WordToMotionDeviceAssign
    {
        None,
        KeyboardWord,
        KeyboardNumber,
        Gamepad,
        MidiController,
    }
        
}