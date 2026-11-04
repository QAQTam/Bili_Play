namespace BiliPlay.Core;

/// <summary>An owned temporary directory with a lifetime lock and crash-recovery cleanup.</summary>
public sealed class TemporarySession : IDisposable
{
    public string DirectoryPath { get; }
    private readonly FileStream _lease;
    private bool _disposed;
    public TemporarySession(string parent)
    {
        Directory.CreateDirectory(parent);EnsureSafe(parent);
        DirectoryPath=Path.Combine(Path.GetFullPath(parent),Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        _lease=new(Path.Combine(DirectoryPath,".biliplay-demo"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
    }
    private static void EnsureSafe(string path)
    {
        for(var item=new DirectoryInfo(path);item is not null;item=item.Parent)
            if((item.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("临时调试目录不能包含链接。");
    }
    private static void DeleteOwned(string path)
    {
        EnsureSafe(path);
        if(!Guid.TryParseExact(Path.GetFileName(path),"N",out _)||!File.Exists(Path.Combine(path,".biliplay-demo")))return;
        foreach(var item in new DirectoryInfo(path).EnumerateFileSystemInfos("*",SearchOption.AllDirectories))
            if((item.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("调试目录包含链接，停止清理。");
        Directory.Delete(path,true);
    }
    public static void CleanAbandoned(string parent)
    {
        if(!Directory.Exists(parent))return;EnsureSafe(parent);
        foreach(var folder in Directory.EnumerateDirectories(parent))
        {
            if(!Guid.TryParseExact(Path.GetFileName(folder),"N",out _)||!File.Exists(Path.Combine(folder,".biliplay-demo")))continue;
            try {using(var lease=new FileStream(Path.Combine(folder,".biliplay-demo"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)){} DeleteOwned(folder);}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_lease.Dispose();
        for(var attempt=0;;attempt++)
        {try{if(Directory.Exists(DirectoryPath))DeleteOwned(DirectoryPath);return;}catch(IOException) when(attempt<8){Thread.Sleep(25);}}
    }
}
