namespace DriftOS.Core.IO
{
    public interface IMouseOutput
    {
        void Move(int dx, int dy);
        void LeftDown();
        void LeftUp();
        void RightDown();
        void RightUp();

        // NEW
        void Scroll(int wheelDelta);   // +120/-120 per notch (vertical)
        void HScroll(int wheelDelta);  // +120 right, -120 left (horizontal)
    }
}

