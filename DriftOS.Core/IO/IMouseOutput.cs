namespace DriftOS.Core.IO;

public interface IMouseOutput
{
    void Move(int dx, int dy);
    void LeftDown();
    void LeftUp();
    void RightDown();
    void RightUp();
}
