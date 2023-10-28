using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using UniGLTF;
using UnityEngine;
using UniVRM10;
using VRMShaders;

namespace Baku.VMagicMirror
{
    //NOTE: このクラスがやることは2つ
    //- 既定のフォルダから利用可能なモーションを持ってきて保持しておく
    //- 指定したアニメーションを再生する
    //- 同時に再生されるアニメーションを最大2つに制限する
    //  - 2つまで許容するのはモーションどうしのブレンドのため
    
    //TODO: ロードを遅延したほうがメモリにちょっと優しいが、特に対策してない
    public class VrmaRepository
    {
        private const string VrmaFileExtension = ".vrma";
        
        private readonly List<VrmaFileItem> _fileItems = new();
        private readonly List<VrmaInstance> _instances = new();

        // _fileItems覧の確認が終わってればtrue
        private bool _fileItemsInitialized;
        // アニメーションデータ自体のロードが始まる/完了済みだとtrue;
        private bool _instanceInitializeStarted;
        private bool _instanceInitialized;

        public VrmaInstance PeekInstance => _instances.Count > 0 ? _instances[0] : null;
        public VrmaInstance PrevInstance => _instances.Count > 1 ? _instances[1] : null;
        
        public IReadOnlyList<string> GetAvailableMotionNames() => GetAvailableFileItems()
            .Select(item => item.FileName)
            .ToArray();
        
        public IReadOnlyList<VrmaFileItem> GetAvailableFileItems()
        {
            if (!_fileItemsInitialized)
            {
                InitializeFileItems();
                _fileItemsInitialized = true;
            }
            return _fileItems;
        }

        public void Initialize()
        {
            if (_instanceInitializeStarted)
            {
                return;
            }

            _instanceInitializeStarted = true;
            InitializeInstancesAsync().Forget();
        }

        public bool TryGetDuration(VrmaFileItem file, out float duration)
        {
            //NOTE: ロード前にめっちゃ急いで呼び出されたら適当に答える
            //。わかりやすさのため
            if (!_instanceInitialized)
            {
                duration = 1f;
                return false;
            }

            var index = _instances.FindIndex(i => i.File.Equals(file));
            if (index < 0)
            {
                duration = 1f;
                return false;
            }

            duration = _instances[index].Animation.clip.length;
            return true;
        }
        
        public void Run(VrmaFileItem file, bool loop)
        {
            //NOTE: ロード前にめっちゃ急いで呼び出されたら対応しない。わかりやすさのため
            if (!_instanceInitialized)
            {
                return;
            }

            var index = _instances.FindIndex(i => i.File.Equals(file));
            if (index < 0)
            {
                return;
            }

            var item =_instances[index];
            _instances.RemoveAt(index);
            _instances.Insert(0, item);
            _instances[0].PlayFromStart(loop);

            //3つ以上は同時再生しない、という不変条件を踏まえた最低限の対応をする
            if (_instances.Count > 2)
            {
                _instances[2].Stop();
            }
        }

        /// <summary> 現在の再生対象のアニメーションを止める。頻繁に呼び出してもよい </summary>
        public void StopCurrentAnimation()
        {
            if (_instances.Count > 0)
            {
                _instances[0].Stop();
            }
        }

        /// <summary> 1つ前に再生していたアニメーションがある場合、それを止める。頻繁に呼び出してもよい </summary>
        public void StopPrevAnimation()
        {
            if (_instances.Count > 1)
            {
                _instances[1].Stop();
            }
        }
        
        public void StopAllAnimations()
        {
            //NOTE: 不変条件として2つまでしか再生してないので、冒頭2つをチェックすればOK
            if (_instances.Count > 0)
            {
                _instances[0].Stop();
            }

            if (_instances.Count > 1)
            {
                _instances[1].Stop();
            }
        }
        
        private void InitializeFileItems()
        {
            _fileItems.Clear();
            var folder = SpecialFiles.MotionsDirectory;
            foreach (var filePath in Directory
                .GetFiles(folder)
                .Where(file => Path.GetExtension(file) == VrmaFileExtension)
                .Select(Path.GetFullPath)
                )
            {
                _fileItems.Add(new VrmaFileItem(filePath));
            }
        }
        
        private async UniTaskVoid InitializeInstancesAsync()
        {
            try
            {
                foreach (var fileItem in GetAvailableFileItems())
                {
                    using var data = new AutoGltfFileParser(fileItem.FilePath).Parse();
                    using var loader = new VrmAnimationImporter(data);
                    var instance = await loader.LoadAsync(new ImmediateCaller());
                    var vrm10AnimationInstance = instance.GetComponent<Vrm10AnimationInstance>();
                    vrm10AnimationInstance.ShowBoxMan(false);

                    _instances.Add(new VrmaInstance(
                        fileItem,
                        vrm10AnimationInstance,
                        instance.GetComponent<Animation>()
                    ));
                }
            }
            catch (Exception ex)
            {
                LogOutput.Instance.Write(ex);
            }
            finally
            {
                _instanceInitialized = true;
            }
        }
    }
}
