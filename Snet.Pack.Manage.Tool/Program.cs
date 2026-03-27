using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Snet.Pack.Manage.Tool
{
    internal class Program
    {
        // dotnet 可执行文件路径（兜底）
        static readonly string DotnetExe =
            File.Exists(@"C:\Program Files\dotnet\dotnet.exe")
                ? @"C:\Program Files\dotnet\dotnet.exe"
                : "dotnet";

        static List<string> GetPackageList() => new()
        {
            "Snet.Mqtt",
            "Snet.Kafka",
            "Snet.RabbitMQ",
            "Snet.Netty",
            "Snet.NetMQ",


            "Snet.AllenBradley",
            "Snet.Beckhoff",
            "Snet.DB",
            "Snet.Delta",
            "Snet.Fatek",
            "Snet.Fuji",
            "Snet.GE",
            "Snet.Inovance",
            "Snet.Invt",
            "Snet.Keyence",
            "Snet.LSis",
            "Snet.MegMeet",
            "Snet.Mitsubishi",
            "Snet.Modbus",
            "Snet.Opc",
            "Snet.Panasonic",
            "Snet.PQDIF",
            "Snet.Siemens",
            "Snet.Omron",
            "Snet.Sim",
            "Snet.TEP",
            "Snet.Toyota",
            "Snet.Vigor",
            "Snet.WeCon",
            "Snet.XinJE",
            "Snet.Yamatake",
            "Snet.Yaskawa",
            "Snet.Yokogawa",
            "Snet.Freedom",
            "Snet.RKC",
            "Snet.Turck",
            "Snet.Fanuc",
        };

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string publicPath = Path.Combine(Directory.GetParent(Directory.GetParent(Directory.GetParent(Directory.GetParent(Directory.GetParent(Environment.CurrentDirectory).FullName).FullName).FullName).FullName).FullName, "pack\\Release");
            Directory.CreateDirectory(publicPath);


            WriteLine("是否需要打包 ZIP？(y/n)", ConsoleColor.Yellow);
            bool zipPack = Console.ReadLine()?.Trim().ToLower() == "y";

            var packages = GetPackageList();
            var successPackages = new List<string>();

            foreach (var package in packages)
            {
                try
                {
                    PublishSinglePackage(package, publicPath);
                    successPackages.Add(package);
                }
                catch (Exception ex)
                {
                    WriteLine(ex.Message, ConsoleColor.Red);
                }
            }

            if (zipPack && successPackages.Any())
            {
                await ZipPackagesAsync(successPackages, publicPath);
            }

            Process.Start("explorer.exe", publicPath);
        }

        // ==============================
        // 单包发布逻辑
        // ==============================
        static void PublishSinglePackage(string packageName, string publicPath)
        {
            string packName = $"{packageName}.Pack";
            string outDir = Path.Combine(publicPath, packName);
            string workDir = Path.Combine(Path.GetTempPath(), packName, "Publish");

            WriteLine($"\n开始下载 {packageName} 库的所有dll", ConsoleColor.Green);
            Console.WriteLine();

            // [1/4] 清理目录
            SafeDelete(workDir);
            SafeDelete(outDir);
            Directory.CreateDirectory(workDir);

            // [2/4] 创建 Runtime 项目
            RunOrThrow("new classlib -n " + packageName + ".Runtime", workDir,
                $"创建项目失败：{packageName}.Runtime");

            string projectDir = Path.Combine(workDir, $"{packageName}.Runtime");

            // [3/4] 添加 NuGet 包（加上源地址）
            RunOrThrow($"add package {packageName} --source https://api.nuget.org/v3/index.json", projectDir,
                $"添加 NuGet 包失败：{packageName}");

            // [4/4] Publish
            RunOrThrow($"publish -c Release -o \"{outDir}\"", projectDir,
                $"发布失败：{packageName}");

            WriteLine($"\n{packageName} 下载完成  ✔", ConsoleColor.Green);
        }


        // ==============================
        // ZIP 打包（并行）
        // ==============================
        static async Task ZipPackagesAsync(IEnumerable<string> packages, string publicPath)
        {
            WriteLine("\n开始 ZIP 打包...", ConsoleColor.Green);

            await Task.WhenAll(packages.Select(pkg =>
                Task.Run(() =>
                {
                    string dir = Path.Combine(publicPath, $"{pkg}.Pack");
                    string zip = dir + ".zip";

                    try
                    {
                        if (File.Exists(zip))
                            File.Delete(zip);

                        ZipFile.CreateFromDirectory(dir, zip, CompressionLevel.Optimal, false);
                        WriteLine($"{Path.GetFileName(zip)}  ✔", ConsoleColor.Green);
                    }
                    catch
                    {
                        WriteLine($"{Path.GetFileName(zip)}  ✘", ConsoleColor.Red);
                    }
                })
            ));
        }

        // ==============================
        // 命令执行器（核心）
        // ==============================
        static void RunOrThrow(string args, string workDir, string errorMessage)
        {
            if (Run(DotnetExe, args, workDir) != 0)
                throw new Exception("  ❌  " + errorMessage);
        }

        static int Run(string file, string args, string workDir, int timeoutMs = 10 * 60 * 1000)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var p = Process.Start(psi)!;

            p.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Console.WriteLine(e.Data);
            };

            p.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    WriteLine(e.Data, ConsoleColor.DarkRed);
            };

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                throw new TimeoutException($"命令执行超时：{file} {args}");
            }

            return p.ExitCode;
        }

        // ==============================
        // 工具方法
        // ==============================
        static void SafeDelete(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }

        /// <summary>
        /// 在控制台中以指定颜色“水平居中”输出一行文本
        /// </summary>
        /// <param name="text">要输出的文本</param>
        /// <param name="color">前景色</param>
        static void WriteLine(string text, ConsoleColor color)
        {
            if (string.IsNullOrEmpty(text))
            {
                Console.WriteLine();
                return;
            }

            string or = text;
            text = text.Replace("\n", "");

            // 保存原有颜色
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = color;

            try
            {
                // 控制台可用宽度
                int windowWidth = Console.WindowWidth;

                // 文本长度（Console 中中文≈2字符宽，但 Console 没法精确算，只能近似）
                int textLength = text.Length;

                // 计算左侧空格数量（最少为 0）
                int leftPadding = Math.Max((windowWidth - textLength) / 2, 0);

                if (or.Contains("\n"))
                {
                    Console.WriteLine();
                }

                // 输出左侧空格 + 文本
                Console.WriteLine(new string(' ', leftPadding) + text);

                if (or.Contains("\n"))
                {
                    Console.WriteLine();
                }
            }
            catch
            {
                // 某些环境（重定向输出、CI）可能访问 WindowWidth 异常，直接降级输出
                Console.WriteLine(text);
            }
            finally
            {
                // 恢复颜色
                Console.ForegroundColor = oldColor;
            }
        }


    }
}
