using System;

namespace TS3Modding.CameraMod
{
    // Both Unity Update and the native pause reader can run first. Keep ownership for the
    // entire frame after restoring, so the same Escape press cannot also pause the game.
    internal sealed class CameraExitGate
    {
        private int consumedFrame=int.MinValue;
        public bool Consume(int frame,bool pressed,bool ownsView,Action restore)
        {
            if(!pressed)return false;
            if(consumedFrame==frame)return true;
            if(!ownsView)return false;
            consumedFrame=frame;restore();return true;
        }
    }
}
