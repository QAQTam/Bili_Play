using BiliPlay.Core;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BiliPlay.Services;

public static class ArtworkCache
{
    private static Lazy<ImageDiskCache> Cache = NewCache();
    private static Lazy<ImageDiskCache> NewCache() => new(()=>new(DebugSession.DirectoryPath is { } root ? Path.Combine(root,"cache","images") : App.IsVisualCheck
        ? Path.Combine(App.VisualCheckDirectory!,"data","cache","images")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),AppProfile.DataDirectoryName,"cache","images")));
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BitmapImage,Uri> Sources = new();
    private static readonly Dictionary<string,WeakReference<BitmapImage>> Memory = [];
    private static readonly LinkedList<(string Key, BitmapImage Image)> Avatars = new();
    public static Uri? SourceUri(BitmapImage? image) => image is null?null:Sources.TryGetValue(image,out var uri)?uri:image.UriSource;
    public static async Task<Uri> ResolveUriAsync(Uri uri)
    {
        if(uri.Scheme!="https"||App.IsVisualCheck||DebugSession.IsActive)return uri;
        var cache=Cache.Value;
        var path=await Task.Run(()=>cache.GetAsync(uri));return path is null?uri:new Uri(path);
    }
    public static BitmapImage CreateAvatar(Uri uri, int width)
    {
        var key=$"{width}:{uri.AbsoluteUri}";
        for(var node=Avatars.First;node is not null;node=node.Next)
        {
            if(node.Value.Key!=key) continue;
            var image=node.Value.Image;Avatars.Remove(node);Avatars.AddFirst((key,image));return image;
        }
        var created=Create(uri,width);Avatars.AddFirst((key,created));
        if(Avatars.Count>64)Avatars.RemoveLast();
        return created;
    }
    public static async Task WarmAsync(IEnumerable<string> images, CancellationToken ct=default)
    {
        if(App.IsVisualCheck||DebugSession.IsActive)return;
        var cache=Cache.Value;
        var urls=images.Distinct(StringComparer.Ordinal).Take(96)
            .Select(value=>Uri.TryCreate(value,UriKind.Absolute,out var uri)?uri:null)
            .OfType<Uri>().Where(Text.IsMediaHost).ToArray();
        try {await Task.WhenAll(urls.Select(uri=>cache.GetAsync(uri,ct)));}
        catch(Exception e) when(e is OperationCanceledException or ObjectDisposedException or IOException) { }
    }
    public static BitmapImage Create(Uri uri,int width)
    {
        var key=$"{width}:{uri.AbsoluteUri}";
        if(Memory.TryGetValue(key,out var weak)&&weak.TryGetTarget(out var existing))return existing;
        var image=new BitmapImage { DecodePixelWidth=width };Sources.Add(image,uri);
        if(Memory.Count>=256)Memory.Clear();Memory[key]=new(image);
        if(uri.Scheme!="https" || App.IsVisualCheck) image.UriSource=uri;
        else _=LoadAsync(image,uri);
        return image;
    }
    private static async Task LoadAsync(BitmapImage image,Uri uri)
    {
        var resolved=await ResolveUriAsync(uri);
        try {image.UriSource=resolved;}
        catch(Exception e) when(e is System.Runtime.InteropServices.COMException or ArgumentException) { }
    }
    public static void ResetScope() { Memory.Clear(); Avatars.Clear(); Sources.Clear(); if(Cache.IsValueCreated)Cache.Value.Dispose(); Cache=NewCache(); }
    public static Task ClearAsync() { Memory.Clear(); Avatars.Clear(); var cache=Cache.Value;return Task.Run(()=>cache.ClearAsync()); }
}
