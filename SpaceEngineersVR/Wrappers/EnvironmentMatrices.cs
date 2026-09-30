using System;
using System.Collections.Generic;
using System.Reflection;
using VRageMath;

namespace SpaceEngineersVR.Wrappers
{
    // Access the real renderer object by named fields; never replace its CLR type pointer.
    public sealed class EnvironmentMatrices
    {
        private readonly object instance;
        private readonly Dictionary<string, FieldInfo> fields = new Dictionary<string, FieldInfo>();

        public EnvironmentMatrices(object instance)
        {
            this.instance = instance ?? throw new ArgumentNullException(nameof(instance));
        }

        private FieldInfo Field<T>(string name)
        {
            if (!fields.TryGetValue(name, out FieldInfo field))
            {
                field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(T))
                    throw new MissingFieldException(instance.GetType().FullName, name + " (" + typeof(T).Name + ")");
                fields.Add(name, field);
            }
            return field;
        }

        public Dictionary<FieldInfo, object> Capture()
        {
            var snapshot = new Dictionary<FieldInfo, object>();
            foreach (var property in typeof(EnvironmentMatrices).GetProperties(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                var field = instance.GetType().GetField(property.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || field.FieldType != property.PropertyType) throw new MissingFieldException(property.Name);
                snapshot.Add(field, field.GetValue(instance));
            }
            return snapshot;
        }
        public void Restore(Dictionary<FieldInfo, object> snapshot)
        {
            foreach (var pair in snapshot) pair.Key.SetValue(instance, pair.Value);
        }

        private T Get<T>(string name) => (T)Field<T>(name).GetValue(instance);
        private void Set<T>(string name, T value) => Field<T>(name).SetValue(instance, value);
        internal Vector3D CameraPosition { get => Get<Vector3D>(nameof(CameraPosition)); set => Set(nameof(CameraPosition), value); }
        internal Matrix ViewAt0 { get => Get<Matrix>(nameof(ViewAt0)); set => Set(nameof(ViewAt0), value); }
        internal Matrix InvViewAt0 { get => Get<Matrix>(nameof(InvViewAt0)); set => Set(nameof(InvViewAt0), value); }
        internal Matrix ViewProjectionAt0 { get => Get<Matrix>(nameof(ViewProjectionAt0)); set => Set(nameof(ViewProjectionAt0), value); }
        internal Matrix InvViewProjectionAt0 { get => Get<Matrix>(nameof(InvViewProjectionAt0)); set => Set(nameof(InvViewProjectionAt0), value); }
        internal Matrix Projection { get => Get<Matrix>(nameof(Projection)); set => Set(nameof(Projection), value); }
        internal Matrix ProjectionForSkybox { get => Get<Matrix>(nameof(ProjectionForSkybox)); set => Set(nameof(ProjectionForSkybox), value); }
        internal Matrix InvProjection { get => Get<Matrix>(nameof(InvProjection)); set => Set(nameof(InvProjection), value); }
        internal MatrixD ViewD { get => Get<MatrixD>(nameof(ViewD)); set => Set(nameof(ViewD), value); }
        internal MatrixD InvViewD { get => Get<MatrixD>(nameof(InvViewD)); set => Set(nameof(InvViewD), value); }
        internal Matrix OriginalProjection { get => Get<Matrix>(nameof(OriginalProjection)); set => Set(nameof(OriginalProjection), value); }
        internal Matrix OriginalProjectionFar { get => Get<Matrix>(nameof(OriginalProjectionFar)); set => Set(nameof(OriginalProjectionFar), value); }
        internal MatrixD ViewProjectionD { get => Get<MatrixD>(nameof(ViewProjectionD)); set => Set(nameof(ViewProjectionD), value); }
        internal MatrixD InvViewProjectionD { get => Get<MatrixD>(nameof(InvViewProjectionD)); set => Set(nameof(InvViewProjectionD), value); }
        internal BoundingFrustumD ViewFrustumClippedD { get => Get<BoundingFrustumD>(nameof(ViewFrustumClippedD)); set => Set(nameof(ViewFrustumClippedD), value); }
        internal BoundingFrustumD ViewFrustumClippedFarD { get => Get<BoundingFrustumD>(nameof(ViewFrustumClippedFarD)); set => Set(nameof(ViewFrustumClippedFarD), value); }
        internal float NearClipping { get => Get<float>(nameof(NearClipping)); set => Set(nameof(NearClipping), value); }
        internal float LargeDistanceFarClipping { get => Get<float>(nameof(LargeDistanceFarClipping)); set => Set(nameof(LargeDistanceFarClipping), value); }
        internal float FarClipping { get => Get<float>(nameof(FarClipping)); set => Set(nameof(FarClipping), value); }
        internal float FovH { get => Get<float>(nameof(FovH)); set => Set(nameof(FovH), value); }
        internal float FovV { get => Get<float>(nameof(FovV)); set => Set(nameof(FovV), value); }
        internal bool LastUpdateWasSmooth { get => Get<bool>(nameof(LastUpdateWasSmooth)); set => Set(nameof(LastUpdateWasSmooth), value); }
    }
}
