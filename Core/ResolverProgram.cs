using System.Text;
using BiliPlay.Core;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
using var resolver = new MusicResolver();
return await resolver.RunCliAsync(args, Console.In, Console.Out, Console.Error, cts.Token);
