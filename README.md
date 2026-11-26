# 磁盘空间清理

磁盘满了，难的往往不是删除，而是看不出空间用在哪。这个小工具在 Windows 上把每一层文件夹的大小摊开比：最大的排在最前面，旁边有一条横条。点进哪一层，才算哪一层。想删的勾上，放进回收站。

界面是中文的。不需要管理员身份。文件列表不会上传到任何地方。

## 能做什么

- 列出本机硬盘、移动硬盘、U 盘和存储卡。网络位置和网盘不列。U 盘和存储卡没有回收站，选盘时会写明。
- 一层一层看文件夹占多少。不小于 100 MB 的文件单独列出来，更小的文件合成一行。
- 勾选文件夹后按「下钻一层」，把这几个文件夹的下一层合成一张列表，每行下面写完整路径。也可以全选。
- 勾选放进「待删除」。换文件夹、换盘，这份清单都还在。关掉程序就清空。
- 确认后放进回收站。某个文件删不掉就记下来，后面的继续。回收站不肯收时停下来问，默认是跳过。没有你的明确确认，不会永久删除。

放进回收站之后，这些空间还被回收站占着。要真正腾出地方，需要再去清空回收站。程序自己不清空回收站。

更细的做法写在 [disk-space-tool.md](disk-space-tool.md)。

## 运行环境

Windows 10 或 Windows 11，64 位。

从源码运行或编译，需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。只拿编译好的程序去用，不用另外安装 .NET。

## 自己编译运行

在仓库根目录：

```powershell
dotnet run --project src/DiskCleaner
```

运行测试：

```powershell
dotnet test
```

打成一个不用安装的程序（大约 60 MB，里面自带运行所需的东西）：

```powershell
dotnet publish src/DiskCleaner/DiskCleaner.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

生成的文件是 `publish/DiskCleaner.exe`。这个目录不会进 Git。双击即可。第一次打开时，Windows 可能提示「已保护你的电脑」，因为程序没有数字签名。点「更多信息」，再点「仍要运行」。

## 目录

| 路径 | 内容 |
| --- | --- |
| `src/DiskCleaner` | 窗口和操作 |
| `src/DiskCleaner.Core` | 读目录、算大小、放进回收站 |
| `tests/DiskCleaner.Core.Tests` | 测试 |
| `disk-space-tool.md` | 设计说明 |

## 许可

[Apache License 2.0](LICENSE)
