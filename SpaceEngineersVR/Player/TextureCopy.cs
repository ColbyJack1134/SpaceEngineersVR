using System;
using SharpDX.Direct3D11;

namespace SpaceEngineersVR.Player
{
    // The native pool forcibly releases borrowed textures at frame end.
    internal sealed class TextureCopy : IDisposable
    {
        private Texture2D texture;
        public ShaderResourceView View { get; private set; }
        public void Store(Texture2D source)
        {
            var description=source.Description;
            if(texture==null || texture.Description.Width!=description.Width || texture.Description.Height!=description.Height || texture.Description.Format!=description.Format)
            {
                Dispose();
                description.BindFlags=BindFlags.ShaderResource;
                texture=new Texture2D(source.Device,description);
                View=new ShaderResourceView(source.Device,texture);
            }
            source.Device.ImmediateContext.CopyResource(source,texture);
        }
        public void Dispose() { View?.Dispose(); View=null; texture?.Dispose(); texture=null; }
    }
}
