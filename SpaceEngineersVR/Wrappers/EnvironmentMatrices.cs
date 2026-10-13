using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using VRageMath;

namespace SpaceEngineersVR.Wrappers
{
    // Access the real renderer object by named fields; never replace its CLR type pointer.
    // Field accessors are resolved once per process: this runs for every billboard, so it must not reflect or box.
    public sealed class EnvironmentMatrices
    {
        private static readonly Type type = AccessTools.TypeByName("VRageRender.MyEnvironmentMatrices");
        private readonly object instance;

        public EnvironmentMatrices(object instance)
        {
            this.instance = instance ?? throw new ArgumentNullException(nameof(instance));
            if (!type.IsInstanceOfType(instance)) throw new ArgumentException(instance.GetType().FullName, nameof(instance));
        }

        private static FieldInfo Field(string name, Type fieldType)
        {
            var field = type?.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != fieldType)
                throw new MissingFieldException(type?.FullName ?? "VRageRender.MyEnvironmentMatrices", name + " (" + fieldType.Name + ")");
            return field;
        }
        private static AccessTools.FieldRef<object, T> Ref<T>(string name) => AccessTools.FieldRefAccess<object, T>(Field(name, typeof(T)));
        private static readonly FieldInfo[] snapshotFields = typeof(EnvironmentMatrices)
            .GetProperties(BindingFlags.Instance | BindingFlags.NonPublic).Select(p => Field(p.Name, p.PropertyType)).ToArray();

        public Dictionary<FieldInfo, object> Capture()
        {
            var snapshot = new Dictionary<FieldInfo, object>();
            foreach (var field in snapshotFields) snapshot.Add(field, field.GetValue(instance));
            return snapshot;
        }
        public void Restore(Dictionary<FieldInfo, object> snapshot)
        {
            foreach (var pair in snapshot) pair.Key.SetValue(instance, pair.Value);
        }

        private static readonly AccessTools.FieldRef<object, Vector3D> cameraPosition = Ref<Vector3D>("CameraPosition");
        internal Vector3D CameraPosition { get => cameraPosition(instance); set => cameraPosition(instance) = value; }
        private static readonly AccessTools.FieldRef<object, Matrix> viewAt0 = Ref<Matrix>("ViewAt0");
        internal Matrix ViewAt0 { get => viewAt0(instance); set => viewAt0(instance) = value; }
        private static readonly AccessTools.FieldRef<object, Matrix> invViewAt0 = Ref<Matrix>("InvViewAt0");
        internal Matrix InvViewAt0 { get => invViewAt0(instance); set => invViewAt0(instance) = value; }
        private static readonly AccessTools.FieldRef<object, Matrix> viewProjectionAt0 = Ref<Matrix>("ViewProjectionAt0");
        internal Matrix ViewProjectionAt0 { get => viewProjectionAt0(instance); set => viewProjectionAt0(instance) = value; }
        private static readonly AccessTools.FieldRef<object, Matrix> invViewProjectionAt0 = Ref<Matrix>("InvViewProjectionAt0");
        internal Matrix InvViewProjectionAt0 { get => invViewProjectionAt0(instance); set => invViewProjectionAt0(instance) = value; }
        private static readonly AccessTools.FieldRef<object, Matrix> projection = Ref<Matrix>("Projection");
        internal Matrix Projection { get => projection(instance); set => projection(instance) = value; }
        private static readonly AccessTools.FieldRef<object, Matrix> projectionForSkybox = Ref<Matrix>("ProjectionForSkybox");
        internal Matrix ProjectionForSkybox { get => projectionForSkybox(instance); set => projectionForSkybox(instance) = value; }
        private static readonly AccessTools.FieldRef<object, Matrix> invProjection = Ref<Matrix>("InvProjection");
        internal Matrix InvProjection { get => invProjection(instance); set => invProjection(instance) = value; }
        private static readonly AccessTools.FieldRef<object, MatrixD> viewD = Ref<MatrixD>("ViewD");
        internal MatrixD ViewD { get => viewD(instance); set => viewD(instance) = value; }
        private static readonly AccessTools.FieldRef<object, MatrixD> invViewD = Ref<MatrixD>("InvViewD");
        internal MatrixD InvViewD { get => invViewD(instance); set => invViewD(instance) = value; }
        private static readonly AccessTools.FieldRef<object, Matrix> originalProjection = Ref<Matrix>("OriginalProjection");
        internal Matrix OriginalProjection { get => originalProjection(instance); set => originalProjection(instance) = value; }
        private static readonly AccessTools.FieldRef<object, Matrix> originalProjectionFar = Ref<Matrix>("OriginalProjectionFar");
        internal Matrix OriginalProjectionFar { get => originalProjectionFar(instance); set => originalProjectionFar(instance) = value; }
        private static readonly AccessTools.FieldRef<object, MatrixD> viewProjectionD = Ref<MatrixD>("ViewProjectionD");
        internal MatrixD ViewProjectionD { get => viewProjectionD(instance); set => viewProjectionD(instance) = value; }
        private static readonly AccessTools.FieldRef<object, MatrixD> invViewProjectionD = Ref<MatrixD>("InvViewProjectionD");
        internal MatrixD InvViewProjectionD { get => invViewProjectionD(instance); set => invViewProjectionD(instance) = value; }
        private static readonly AccessTools.FieldRef<object, BoundingFrustumD> viewFrustumClippedD = Ref<BoundingFrustumD>("ViewFrustumClippedD");
        internal BoundingFrustumD ViewFrustumClippedD { get => viewFrustumClippedD(instance); set => viewFrustumClippedD(instance) = value; }
        private static readonly AccessTools.FieldRef<object, BoundingFrustumD> viewFrustumClippedFarD = Ref<BoundingFrustumD>("ViewFrustumClippedFarD");
        internal BoundingFrustumD ViewFrustumClippedFarD { get => viewFrustumClippedFarD(instance); set => viewFrustumClippedFarD(instance) = value; }
        private static readonly AccessTools.FieldRef<object, float> nearClipping = Ref<float>("NearClipping");
        internal float NearClipping { get => nearClipping(instance); set => nearClipping(instance) = value; }
        private static readonly AccessTools.FieldRef<object, float> largeDistanceFarClipping = Ref<float>("LargeDistanceFarClipping");
        internal float LargeDistanceFarClipping { get => largeDistanceFarClipping(instance); set => largeDistanceFarClipping(instance) = value; }
        private static readonly AccessTools.FieldRef<object, float> farClipping = Ref<float>("FarClipping");
        internal float FarClipping { get => farClipping(instance); set => farClipping(instance) = value; }
        private static readonly AccessTools.FieldRef<object, float> fovH = Ref<float>("FovH");
        internal float FovH { get => fovH(instance); set => fovH(instance) = value; }
        private static readonly AccessTools.FieldRef<object, float> fovV = Ref<float>("FovV");
        internal float FovV { get => fovV(instance); set => fovV(instance) = value; }
        private static readonly AccessTools.FieldRef<object, bool> lastUpdateWasSmooth = Ref<bool>("LastUpdateWasSmooth");
        internal bool LastUpdateWasSmooth { get => lastUpdateWasSmooth(instance); set => lastUpdateWasSmooth(instance) = value; }
    }
}
