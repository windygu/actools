﻿using System.Linq;
using AcTools.Kn5File;
using AcTools.Render.Base;
using AcTools.Render.Base.Cameras;
using AcTools.Render.Base.Objects;
using AcTools.Render.Base.Structs;
using AcTools.Render.Base.Utils;
using AcTools.Render.Kn5Specific.Materials;
using AcTools.Render.Kn5Specific.Textures;
using SlimDX;

namespace AcTools.Render.Kn5Specific.Objects {
    public class Kn5RenderableObject : TrianglesRenderableObject<InputLayouts.VerticePNTG> {
        public static bool FlipByX = true;

        public readonly bool IsCastingShadows;

        public readonly Kn5Node OriginalNode;
        private IRenderableMaterial _material;

        private static InputLayouts.VerticePNTG[] Convert(Kn5Node.Vertice[] vertices) {
            if (FlipByX) {
                return vertices.Select(x => new InputLayouts.VerticePNTG(
                        x.Co.ToVector3FixX(),
                        x.Normal.ToVector3FixX(),
                        x.Uv.ToVector2(),
                        x.Tangent.ToVector3Tangent())).ToArray();
            }

            return vertices.Select(x => new InputLayouts.VerticePNTG(
                    x.Co.ToVector3(),
                    x.Normal.ToVector3(),
                    x.Uv.ToVector2(),
                    x.Tangent.ToVector3())).ToArray();
        }

        private static ushort[] Convert(ushort[] indices) {
            return FlipByX ? indices.ToIndicesFixX() : indices;
        }

        private readonly bool _isTransparent;

        public Kn5RenderableObject(Kn5Node node, DeviceContextHolder holder)
                : base(Convert(node.Vertices), Convert(node.Indices)) {
            OriginalNode = node;
            IsCastingShadows = node.CastShadows;

            var materialsProvider = holder.Get<Kn5MaterialsProvider>();
            _material = materialsProvider.GetMaterial(OriginalNode.MaterialId);

            if (IsEnabled && (!OriginalNode.Active || !OriginalNode.IsVisible || !OriginalNode.IsRenderable)) {
                IsEnabled = false;
            }

            _isTransparent = OriginalNode.IsTransparent && _material.IsBlending;
        }

        public void SwitchToMirror(DeviceContextHolder holder) {
            var materialsProvider = holder.Get<Kn5MaterialsProvider>();
            _material = materialsProvider.GetMirrorMaterial();
        }

        public Vector3? Emissive { get; set; }

        public void SetEmissive(Vector3? color) {
            Emissive = color;
        }

        protected override void Initialize(DeviceContextHolder contextHolder) {
            base.Initialize(contextHolder);
            _material.Initialize(contextHolder);
        }

        protected override void DrawInner(DeviceContextHolder contextHolder, ICamera camera, SpecialRenderMode mode) {
            if (_isTransparent &&
                    !mode.HasFlag(SpecialRenderMode.SimpleTransparent) &&
                    mode != SpecialRenderMode.DeferredTransparentForw &&
                    mode != SpecialRenderMode.DeferredTransparentDef &&
                    mode != SpecialRenderMode.DeferredTransparentMask) return;

            if (mode == SpecialRenderMode.Shadow && !IsCastingShadows) return;
            if (!_material.Prepare(contextHolder, mode)) return;

            base.DrawInner(contextHolder, camera, mode);

            if (Emissive.HasValue) {
                (_material as IEmissiveMaterial)?.SetEmissiveNext(Emissive.Value);
            }

            _material.SetMatrices(ParentMatrix, camera);
            _material.Draw(contextHolder, Indices.Length, mode);
        }

        public override void Dispose() {
            base.Dispose();
            _material.Dispose();
        }
    }
}
