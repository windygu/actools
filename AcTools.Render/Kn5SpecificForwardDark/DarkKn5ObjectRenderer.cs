﻿using System;
using System.Drawing;
using System.Linq;
using AcTools.Render.Base.Cameras;
using AcTools.Render.Base.Materials;
using AcTools.Render.Base.Objects;
using AcTools.Render.Base.PostEffects;
using AcTools.Render.Base.Reflections;
using AcTools.Render.Base.Shadows;
using AcTools.Render.Base.TargetTextures;
using AcTools.Render.Base.Utils;
using AcTools.Render.Kn5Specific.Objects;
using AcTools.Render.Kn5SpecificForward;
using AcTools.Render.Kn5SpecificForwardDark.Materials;
using AcTools.Render.Shaders;
using AcTools.Utils;
using AcTools.Utils.Helpers;
using JetBrains.Annotations;
using SlimDX;
using SlimDX.Direct3D11;
using SlimDX.DXGI;

namespace AcTools.Render.Kn5SpecificForwardDark {
    public partial class DarkKn5ObjectRenderer : ToolsKn5ObjectRenderer {
        public Color LightColor { get; set; } = Color.FromArgb(200, 180, 180);

        public Color AmbientDown { get; set; } = Color.FromArgb(150, 180, 180);

        public Color AmbientUp { get; set; } = Color.FromArgb(180, 180, 150);

        /*
        public Color LightColor { get; set; } = Color.FromArgb(201, 201, 167);
        public Color AmbientDown { get; set; } = Color.FromArgb(82, 136, 191);
        public Color AmbientUp { get; set; } = Color.FromArgb(191, 191, 159);*/

        private bool _flatMirror;

        public bool FlatMirror {
            get { return _flatMirror; }
            set {
                if (value == _flatMirror) return;
                _flatMirror = value;
                OnPropertyChanged();
                RecreateFlatMirror();
                UpdateBlurredFlatMirror();
                IsDirty = true;
            }
        }

        private bool _flatMirrorBlurred;

        public bool FlatMirrorBlurred {
            get { return _flatMirrorBlurred; }
            set {
                if (Equals(value, _flatMirrorBlurred)) return;
                _flatMirrorBlurred = value;
                OnPropertyChanged();
                UpdateBlurredFlatMirror();
                IsDirty = true;
            }
        }

        private TargetResourceTexture _mirrorBuffer, _mirrorBlurBuffer, _temporaryBuffer;
        private TargetResourceDepthTexture _mirrorDepthBuffer;

        private void UpdateBlurredFlatMirror() {
            var use = FlatMirror && FlatMirrorBlurred;
            if (use == (_mirrorBuffer != null)) return;

            if (use) {
                _mirrorBuffer = TargetResourceTexture.Create(Format.R16G16B16A16_Float);
                _mirrorBlurBuffer = TargetResourceTexture.Create(Format.R16G16B16A16_Float);
                _temporaryBuffer = TargetResourceTexture.Create(Format.R16G16B16A16_Float);
                _mirrorDepthBuffer = TargetResourceDepthTexture.Create();

                if (!InitiallyResized) return;
                ResizeMirrorBuffers();
            } else {
                DisposeHelper.Dispose(ref _mirrorBuffer);
                DisposeHelper.Dispose(ref _mirrorBlurBuffer);
                DisposeHelper.Dispose(ref _temporaryBuffer);
                DisposeHelper.Dispose(ref _mirrorDepthBuffer);
            }
        }

        private void ResizeMirrorBuffers() {
            _mirrorBuffer?.Resize(DeviceContextHolder, Width, Height, null);
            _mirrorDepthBuffer?.Resize(DeviceContextHolder, Width, Height, null);
            _mirrorBlurBuffer?.Resize(DeviceContextHolder, ActualWidth, ActualHeight, null);
            _temporaryBuffer?.Resize(DeviceContextHolder, ActualWidth, ActualHeight, null);
        }

        protected override void ResizeInner() {
            base.ResizeInner();
            ResizeMirrorBuffers();
        }

        private bool _opaqueGround = true;

        public bool OpaqueGround {
            get { return _opaqueGround; }
            set {
                if (Equals(value, _opaqueGround)) return;
                _opaqueGround = value;
                OnPropertyChanged();
                RecreateFlatMirror();
            }
        }

        public override bool ShowWireframe {
            get { return base.ShowWireframe; }
            set {
                base.ShowWireframe = value;
                (_carWrapper?.ElementAtOrDefault(0) as FlatMirror)?.SetInvertedRasterizerState(
                        value ? DeviceContextHolder.States.WireframeInvertedState : null);
            }
        }

        private float _lightBrightness = 1.5f;

        public float LightBrightness {
            get { return _lightBrightness; }
            set {
                if (Equals(value, _lightBrightness)) return;
                _lightBrightness = value;
                OnPropertyChanged();
            }
        }

        private float _ambientBrightness = 2f;

        public float AmbientBrightness {
            get { return _ambientBrightness; }
            set {
                if (Equals(value, _ambientBrightness)) return;
                _ambientBrightness = value;
                OnPropertyChanged();
            }
        }

        public DarkKn5ObjectRenderer(CarDescription car, string showroomKn5 = null) : base(car, showroomKn5) {
            // UseMsaa = true;
            VisibleUi = false;
            UseSprite = false;
            AllowSkinnedObjects = true;

            //BackgroundColor = Color.FromArgb(10, 15, 25);
            //BackgroundColor = Color.FromArgb(220, 140, 100);

            BackgroundColor = Color.FromArgb(220, 220, 220);
            EnableShadows = EffectDarkMaterial.EnableShadows;
        }

        protected override void OnBackgroundColorChanged() {
            base.OnBackgroundColorChanged();
            UiColor = BackgroundColor.GetBrightness() > 0.5 ? Color.Black : Color.White;
        }

        private static float[] GetSplits(int number, float carSize) {
            switch (number) {
                case 1:
                    return new[] { carSize };
                case 2:
                    return new[] { 5f, 20f };
                case 3:
                    return new[] { 5f, 20f, 50f };
                case 4:
                    return new[] { 5f, 20f, 50f, 200f };
                default:
                    return new[] { 10f };
            }
        }

        private Kn5RenderableCar _car;
        private FlatMirror _mirror; 
        private RenderableList _carWrapper;

        private void RecreateFlatMirror() {
            if (_carWrapper == null) return;

            var replaceMode = _carWrapper.ElementAtOrDefault(0) is FlatMirror;
            if (replaceMode) {
                _carWrapper[0].Dispose();
                _carWrapper.RemoveAt(0);
            }

            var mirrorPlane = new Plane(Vector3.Zero, Vector3.UnitY);
            _mirror = FlatMirror && CarNode != null ? new FlatMirror(CarNode, mirrorPlane) :
                    new FlatMirror(mirrorPlane, OpaqueGround);
            if (FlatMirror && ShowWireframe) {
                _mirror.SetInvertedRasterizerState(DeviceContextHolder.States.WireframeInvertedState);
            }

            _carWrapper.Insert(0, _mirror);

            if (replaceMode) {
                _carWrapper.UpdateBoundingBox();
            }
        }

        protected override void ExtendCar(Kn5RenderableCar car, RenderableList carWrapper) {
            if (_car != null) {
                _car.ObjectsChanged -= OnCarObjectsChanged;
            }

            base.ExtendCar(car, carWrapper);

            _car = car;
            if (_car != null) {
                _car.ObjectsChanged += OnCarObjectsChanged;
            }

            _carWrapper = carWrapper;
            RecreateFlatMirror();

            if (_meshDebug) {
                UpdateMeshDebug(car);
            }
        }

        private void OnCarObjectsChanged(object sender, EventArgs e) {
            RecreateFlatMirror();
        }

        protected override void PrepareCamera(BaseCamera camera) {
            base.PrepareCamera(camera);

            var orbit = camera as CameraOrbit;
            if (orbit != null) {
                orbit.MinBeta = -0.1f;
                orbit.MinY = 0.05f;
            }

            camera.DisableFrustum = true;
        }

        protected override IMaterialsFactory GetMaterialsFactory() {
            return new MaterialsProviderDark();
        }

        private ShadowsDirectional _shadows;

        protected override ShadowsDirectional CreateShadows() {
            _shadows = new ShadowsDirectional(EffectDarkMaterial.ShadowMapSize,
                    GetSplits(EffectDarkMaterial.NumSplits, CarNode?.BoundingBox?.GetSize().Length() ?? 4f));
            return _shadows;
        }

        protected override ReflectionCubemap CreateReflectionCubemap() {
            return new ReflectionCubemap(1024);
        }

        [CanBeNull]
        private EffectDarkMaterial _effect;
        private Vector3 _light;

        private float _reflectionPower = 0.6f;

        public float ReflectionPower {
            get { return _reflectionPower; }
            set {
                if (Equals(value, _reflectionPower)) return;
                _reflectionPower = value;
                OnPropertyChanged();
            }
        }

        protected override void UpdateShadows(ShadowsDirectional shadows, Vector3 center) {
            shadows.SetSplits(DeviceContextHolder, GetSplits(EffectDarkMaterial.NumSplits, CarNode?.BoundingBox?.GetSize().Length() ?? 4f));
            base.UpdateShadows(shadows, center);

            if (_effect == null) {
                _effect = DeviceContextHolder.GetEffect<EffectDarkMaterial>();
            }

            _effect.FxShadowMaps.SetResourceArray(shadows.Splits.Take(EffectDarkMaterial.NumSplits).Select(x => x.View).ToArray());
            _effect.FxShadowViewProj.SetMatrixArray(
                    shadows.Splits.Take(EffectDarkMaterial.NumSplits).Select(x => x.ShadowTransform).ToArray());
        }

        protected override void DrawPrepareEffect(Vector3 eyesPosition, Vector3 light, ShadowsDirectional shadows, ReflectionCubemap reflection) {
            if (_effect == null) {
                _effect = DeviceContextHolder.GetEffect<EffectDarkMaterial>();
            }

            _effect.FxEyePosW.Set(eyesPosition);

            _light = light;
            _effect.FxLightDir.Set(light);

            _effect.FxLightColor.Set(LightColor.ToVector3() * LightBrightness);
            _effect.FxAmbientDown.Set(AmbientDown.ToVector3() * AmbientBrightness);
            _effect.FxAmbientRange.Set((AmbientUp.ToVector3() - AmbientDown.ToVector3()) * AmbientBrightness);
            _effect.FxBackgroundColor.Set(BackgroundColor.ToVector3());

            if (FlatMirror) {
                _effect.FxFlatMirrorPower.Set(ReflectionPower);
            }

            if (reflection != null) {
                _effect.FxReflectionCubemap.SetResource(reflection.View);
            }
        }

        private bool _suspensionDebug;

        public bool SuspensionDebug {
            get { return _suspensionDebug; }
            set {
                if (Equals(value, _suspensionDebug)) return;
                _suspensionDebug = value;
                OnPropertyChanged();
            }
        }

        private bool _meshDebug;

        public bool MeshDebug {
            get { return _meshDebug; }
            set {
                if (Equals(value, _meshDebug)) return;
                _meshDebug = value;
                OnPropertyChanged();
                UpdateMeshDebug(CarNode);
            }
        }

        private void UpdateMeshDebug([CanBeNull] Kn5RenderableCar carNode) {
            if (carNode != null) {
                carNode.DebugMode = _meshDebug;
            }
        }

        private void DrawMirror() {
            DeviceContext.OutputMerger.DepthStencilState = DeviceContextHolder.States.LessEqualDepthState;
            _mirror.DrawReflection(DeviceContextHolder, ActualCamera, SpecialRenderMode.Simple);

            DeviceContext.OutputMerger.DepthStencilState = DeviceContextHolder.States.ReadOnlyDepthState;
            _mirror.DrawReflection(DeviceContextHolder, ActualCamera, SpecialRenderMode.SimpleTransparent);
        }

        protected override void DrawScene() {
            // TODO: support more than one car?

            DeviceContext.OutputMerger.DepthStencilState = null;
            DeviceContext.OutputMerger.BlendState = null;
            DeviceContext.Rasterizer.State = GetRasterizerState();

            var carNode = CarNode;

            // draw reflection if needed
            if (FlatMirror && _mirror != null) {
                if (_effect == null) {
                    _effect = DeviceContextHolder.GetEffect<EffectDarkMaterial>();
                }

                _effect.FxLightDir.Set(new Vector3(_light.X, -_light.Y, _light.Z));

                if (FlatMirrorBlurred) {
                    DeviceContext.ClearDepthStencilView(_mirrorDepthBuffer.DepthView, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 1.0f, 0);
                    DeviceContext.ClearRenderTargetView(_mirrorBuffer.TargetView, BackgroundColor);

                    DeviceContext.OutputMerger.SetTargets(_mirrorDepthBuffer.DepthView, _mirrorBuffer.TargetView);

                    DrawMirror();

                    DeviceContext.Rasterizer.SetViewports(OutputViewport);

                    if (UseFxaa) {
                        DeviceContextHolder.GetHelper<FxaaHelper>().Draw(DeviceContextHolder, _mirrorBuffer.View, _mirrorBlurBuffer.TargetView);
                        DeviceContextHolder.GetHelper<BlurHelper>()
                                           .BlurFlatMirror(DeviceContextHolder, _mirrorBlurBuffer, _temporaryBuffer, ActualCamera.ViewProjInvert,
                                                   _mirrorDepthBuffer.View, 60f);
                    } else {
                        DeviceContextHolder.GetHelper<BlurHelper>()
                                           .BlurFlatMirror(DeviceContextHolder, _mirrorBuffer, _temporaryBuffer, ActualCamera.ViewProjInvert,
                                                   _mirrorDepthBuffer.View, 60f, target: _mirrorBlurBuffer);
                    }

                    DeviceContextHolder.GetHelper<BlurHelper>()
                                       .BlurFlatMirror(DeviceContextHolder, _mirrorBlurBuffer, _temporaryBuffer, ActualCamera.ViewProjInvert,
                                               _mirrorDepthBuffer.View, 12f);

                    DeviceContext.Rasterizer.SetViewports(Viewport);
                    DeviceContext.OutputMerger.SetTargets(DepthStencilView, InnerBuffer.TargetView);
                } else {
                    DrawMirror();
                }

                _effect.FxLightDir.Set(_light);
            }

            // draw a scene, apart from car
            // TODO

            // draw a mirror
            if (_mirror != null) {
                if (!FlatMirror) {
                    _mirror.SetMode(DeviceContextHolder, FlatMirrorMode.BackgroundGround);
                    _mirror.Draw(DeviceContextHolder, ActualCamera, SpecialRenderMode.Simple);
                } else if (FlatMirrorBlurred && _mirrorBuffer != null) {
                    if (_effect == null) {
                        _effect = DeviceContextHolder.GetEffect<EffectDarkMaterial>();
                    }

                    _effect.FxScreenSize.Set(new Vector4(Width, Height, 1f / Width, 1f / Height));
                    // _effect.FxWorldViewProjInv.SetMatrix(ActualCamera.ViewProjInvert);
                    _mirror.SetMode(DeviceContextHolder, FlatMirrorMode.TextureMirror);
                    _mirror.Draw(DeviceContextHolder, ActualCamera, _mirrorBlurBuffer.View, null, null);
                } else {
                    _mirror.SetMode(DeviceContextHolder, FlatMirrorMode.TransparentMirror);
                    _mirror.Draw(DeviceContextHolder, ActualCamera, SpecialRenderMode.SimpleTransparent);
                }
            }

            // draw car
            if (carNode == null) return;

            // shadows
            carNode.DrawAmbientShadows(DeviceContextHolder, ActualCamera);
            
            // car itself
            DeviceContext.OutputMerger.DepthStencilState = DeviceContextHolder.States.LessEqualDepthState;
            carNode.Draw(DeviceContextHolder, ActualCamera, SpecialRenderMode.Simple);

            DeviceContext.OutputMerger.DepthStencilState = DeviceContextHolder.States.ReadOnlyDepthState;
            carNode.Draw(DeviceContextHolder, ActualCamera, SpecialRenderMode.SimpleTransparent);

            // debug stuff
            if (SuspensionDebug) {
                carNode.DrawSuspensionDebugStuff(DeviceContextHolder, ActualCamera);
            }

            if (carNode.IsColliderVisible) {
                carNode.DrawCollidersDebugStuff(DeviceContextHolder, ActualCamera);
            }
        }

        private bool _setCameraHigher = true;

        public bool SetCameraHigher {
            get { return _setCameraHigher; }
            set {
                if (Equals(value, _setCameraHigher)) return;
                _setCameraHigher = value;
                OnPropertyChanged();
            }
        }

        protected override Vector3 AutoAdjustedTarget => base.AutoAdjustedTarget + Vector3.UnitY * (SetCameraHigher ? 0f : 0.2f);

        public override void Dispose() {
            base.Dispose();
            DisposeHelper.Dispose(ref _mirrorBuffer);
            DisposeHelper.Dispose(ref _mirrorBlurBuffer);
            DisposeHelper.Dispose(ref _temporaryBuffer);
            DisposeHelper.Dispose(ref _mirrorDepthBuffer);
        }
    }
}