using System.Runtime.InteropServices;
namespace PingWatchdog;
internal static class SampleLogRenderer
{
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Range { public IntPtr Hdc, Target; public Rect Area, Page; public int Start, End; }
    [DllImport("user32.dll", EntryPoint="SendMessageW")] static extern IntPtr Format(IntPtr hwnd,int message,IntPtr render,ref Range range);
    [DllImport("user32.dll", EntryPoint="SendMessageW")] static extern IntPtr Release(IntPtr hwnd,int message,IntPtr render,IntPtr data);
    internal static void Draw(RichTextBox box, Graphics g, Size size)
    {
        int dpi=box.DeviceDpi;
        var rect=new Rect { Left=30,Top=15,Right=(size.Width-3)*1440/dpi,Bottom=size.Height*1440/dpi };
        int first=box.GetCharIndexFromPosition(new Point(2,2));
        int line=box.GetLineFromCharIndex(first);
        var dc=g.GetHdc();
        try {
            var range=new Range { Hdc=dc,Target=dc,Area=rect,Page=rect,Start=Math.Max(0,box.GetFirstCharIndexFromLine(line)),End=-1 };
            Format(box.Handle,0x439,(IntPtr)1,ref range);
        } finally { Release(box.Handle,0x439,IntPtr.Zero,IntPtr.Zero);g.ReleaseHdc(dc); }
    }
}