using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SpaceEngineersVR.Wrappers;

namespace SpaceEngineersVR.Diagnostics
{
    public static class CompatibilityProbe
    {
        // Runs only metadata lookups. Does not patch methods, initialize D3D or construct Player.
        public static bool Run(Action<string> log)
        {
            bool ok = true;
            Type[] wrappers = { typeof(MyRender11), typeof(MyManagers), typeof(MyBorrowedRwTextureManager),
                typeof(BorrowedRtvTexture), typeof(MyBackbuffer), typeof(MyRenderContext), typeof(MyCommon) };
            foreach (Type wrapper in wrappers)
            {
                try
                {
                    RuntimeHelpers.RunClassConstructor(wrapper.TypeHandle);
                    foreach (FieldInfo field in wrapper.GetFields(BindingFlags.Static | BindingFlags.NonPublic))
                    {
                        if ((typeof(MemberInfo).IsAssignableFrom(field.FieldType) || typeof(Delegate).IsAssignableFrom(field.FieldType))
                            && field.GetValue(null) == null)
                            throw new MissingMemberException(wrapper.Name, field.Name);
                    }
                    log("PASS wrapper: " + wrapper.Name);
                }
                catch (Exception ex)
                {
                    ok = false;
                    log("FAIL wrapper: " + wrapper.Name + " - " + ex.GetBaseException().Message);
                }
            }

            Type render = AccessTools.TypeByName("VRageRender.MyRender11");
            foreach (string name in new[] { "Present", "DrawScene" })
            {
                MethodInfo method = render == null ? null : AccessTools.Method(render, name);
                bool valid = method != null && method.IsStatic;
                ok &= valid;
                log((valid ? "PASS" : "FAIL") + " render patch target: " + name);
            }

            Type matrices = AccessTools.TypeByName("VRageRender.MyEnvironmentMatrices");
            if (matrices == null)
            {
                Type environment = AccessTools.TypeByName("VRageRender.MyEnvironment");
                matrices = environment == null ? null : AccessTools.Field(environment, "Matrices")?.FieldType;
            }
            foreach (PropertyInfo property in typeof(EnvironmentMatrices).GetProperties(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                FieldInfo field = matrices == null ? null : AccessTools.Field(matrices, property.Name);
                bool valid = field != null && field.FieldType == property.PropertyType;
                ok &= valid;
                log((valid ? "PASS" : "FAIL") + " camera field: " + property.Name);
            }
            if (render != null)
            {
                foreach (string name in new[] { "DrawGameScene", "SetupCameraMatrices", "FullDraw", "CreateScreenResources", "ProcessMessageQueue", "ProcessUpdates", "UpdateGameScene" })
                    foreach (MethodInfo method in render.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where(m => m.Name == name))
                        log("SIGNATURE " + method);
            }
            log(ok ? "Renderer metadata: compatible" : "Renderer metadata: PORT REQUIRED; VR rendering must remain disabled.");
            return ok;
        }
    }
}
