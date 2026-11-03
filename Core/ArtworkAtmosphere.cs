namespace BiliPlay.Core;

public readonly record struct ArtworkColor(byte R, byte G, byte B);
public sealed record ArtworkPalette(ArtworkColor Primary, ArtworkColor Secondary);
public static class ArtworkAtmosphere
{
    public static ArtworkPalette Neutral { get; } = new(new(73,103,141),new(124,91,115));
    public static ArtworkPalette Sample(ReadOnlySpan<byte> bgra)
    {
        var bins = new Dictionary<int,(long R,long G,long B,int Count)>();
        for(var i=0;i+3<bgra.Length;i+=4)
        {
            if(bgra[i+3]<200) continue;
            var b=bgra[i]; var g=bgra[i+1]; var r=bgra[i+2];
            var high=Math.Max(r,Math.Max(g,b)); var low=Math.Min(r,Math.Min(g,b));
            if(high<24 || low>238) continue;
            var key=(r/32)*64+(g/32)*8+b/32;
            var bin=bins.GetValueOrDefault(key); bins[key]=(bin.R+r,bin.G+g,bin.B+b,bin.Count+1);
        }
        if(bins.Count==0) return Neutral;
        var colors=bins.Values.OrderByDescending(x=>x.Count).Select(x=>new ArtworkColor((byte)(x.R/x.Count),(byte)(x.G/x.Count),(byte)(x.B/x.Count))).ToArray();
        var first=colors[0];
        var second=colors.Skip(1).FirstOrDefault(x=>Math.Abs(x.R-first.R)+Math.Abs(x.G-first.G)+Math.Abs(x.B-first.B)>100, first);
        return new(first,second);
    }
    public static ArtworkColor At(ArtworkPalette palette,double x,double y,bool dark)
    {
        var first=Math.Exp(-((x-.2)*(x-.2)/.28+(y-.52)*(y-.52)/.4))*(dark?.38:.12);
        var second=Math.Exp(-((x-.92)*(x-.92)/.22+(y-.12)*(y-.12)/.55))*(dark?.27:.09);
        byte Mix(byte a,byte b)
        {
            var color=(dark?30:244)*(1-first-second)+a*first+b*second;
            return (byte)Math.Clamp(Math.Round(color),dark?18:214,dark?80:250);
        }
        return new(Mix(palette.Primary.R,palette.Secondary.R),Mix(palette.Primary.G,palette.Secondary.G),Mix(palette.Primary.B,palette.Secondary.B));
    }
    public static byte[] Render(ArtworkPalette palette,bool dark,int width=192,int height=120)
    {
        var pixels=new byte[checked(width*height*4)];
        for(var y=0;y<height;y++) for(var x=0;x<width;x++)
        {
            var color=At(palette,(double)x/Math.Max(1,width-1),(double)y/Math.Max(1,height-1),dark); var i=(y*width+x)*4;
            pixels[i]=color.B; pixels[i+1]=color.G; pixels[i+2]=color.R; pixels[i+3]=255;
        }
        return pixels;
    }
}
