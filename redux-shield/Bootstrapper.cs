using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Globalization;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Text;
using System.IO;
using System;

namespace ReduxShield;

public class Bootstrapper
{
	[DllImport("kernel32.dll")]
	static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
	
	[DllImport("kernel32.dll")]
	static extern uint SuspendThread(IntPtr hThread);
	
	[DllImport("kernel32.dll")]
	static extern int ResumeThread(IntPtr hThread);
	
	[DllImport("kernel32", CharSet = CharSet.Auto,SetLastError = true)]
	static extern bool CloseHandle(IntPtr handle);
	
	[Flags]
	private enum ThreadAccess
	{
		SUSPEND_RESUME = 0x0002
	}

	public async Task Initialize()
	{
		Console.Title = $"{ProgramName} v{Version}";
		VerifyRequiredAdminRights();
		
		ValidateConfig();
		StartGtaWatcher();
		
		Console.ForegroundColor = ConsoleColor.DarkMagenta;
		Console.WriteLine(Language["start-message"], Version);
		Console.ResetColor();
		
		await Task.Delay(-1);
	}

	private static void VerifyRequiredAdminRights()
	{
		using var identity = WindowsIdentity.GetCurrent();
		var principal = new WindowsPrincipal(identity);
		var isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
		if (isAdmin) return;
		
		Console.ForegroundColor = ConsoleColor.Yellow;
		Console.WriteLine($">> {Language["restart-with-admin"]}");
		Console.ResetColor();
		Thread.Sleep(1000);
		
		var process = new ProcessStartInfo { FileName = ExecutablePath, Verb = "runas" };
		try
		{
			Process.Start(process);
			Environment.Exit(0);
		}
		catch
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($">> {Language["failed-to-start"]}");
			Console.WriteLine($">> {Language["please-run-as-admin"]}");
			Console.ResetColor();
			Thread.Sleep(5000);
			Environment.Exit(1);
		}
	}
	
	private static void ValidateConfig()
	{
		if (ExecutableDir == null)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($">> {Language["unable-to-get-executable-directory"]}");
			Console.ResetColor();
			Thread.Sleep(5000);
			Environment.Exit(1);
		}
		
		var configPath = Path.Combine(ExecutableDir, ConfigFileName);
		if (!File.Exists(configPath))
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine($">> {Language["config-file-not-found"]}");
			Console.ResetColor();
			File.WriteAllText(configPath, DefaultConfig.ToString());
		}
		else
		{
			var config = File.ReadAllLines(configPath);
			foreach (var line in config)
			{
				if (line.StartsWith("//")) continue;
				
				var split = line.Split('=');
				if (split.Length != 2) continue;
				
				var key = split[0];
				var value = split[1];
				
				switch (key)
				{
					case "ReduxPath": _reduxPath = FixPath(value); break;
					case "GtaVPath": _gtaVPath = FixPath(value); break;
					case "TeraCopyPath": _teraCopyPath = FixPath(value); break;
					default:
						Console.ForegroundColor = ConsoleColor.Yellow;
						Console.WriteLine($"<< {Language["unknown-config-key"]}", key);
						Console.ResetColor();
						break;
				}
			}
		}
		
		if (string.IsNullOrEmpty(_reduxPath) || string.IsNullOrEmpty(_gtaVPath))
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($">> {Language["invalid-config-file"]}");
			Console.ResetColor();
			Thread.Sleep(5000);
			Environment.Exit(1);
		}
		
		if (!Directory.Exists(_reduxPath) || !Directory.Exists(_gtaVPath))
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($">> {Language["invalid-paths-in-config-file"]}");
			Console.ResetColor();
			Thread.Sleep(5000);
			Environment.Exit(1);
		}
		
		var altVBackupDir = $"{SystemDrive}/Users/{SystemUserName}/AppData/Local/altv-majestic/backup";
		if (!Directory.Exists(altVBackupDir))
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($">> {Language["altv-backup-dir-not-found"]}");
			Console.ResetColor();
			Thread.Sleep(5000);
			Environment.Exit(1);
		}
	}
	
	private static void StartGtaWatcher()
	{
		var wqlStartQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName = \"GTA5.exe\"");
		_gtaStartWatcher = new ManagementEventWatcher(wqlStartQuery);
	
		_gtaStartWatcher.EventArrived += startWatch_EventArrived;
		_gtaStartWatcher.Start();
	}
	
	private static void StartExitTeraCopyWatcher()
	{
		var wqlExitQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace WHERE ProcessName = \"TeraCopy.exe\"");
		_teraCopyExitWatcher = new ManagementEventWatcher(wqlExitQuery);
	
		_teraCopyExitWatcher.EventArrived += teraCopyExit_EventArrived;
		_teraCopyExitWatcher.Start();
	}
	
	private static void StartExitWatcher()
	{
		var wqlExitQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace WHERE ProcessName = \"GTA5.exe\"");
		_gtaExitWatcher = new ManagementEventWatcher(wqlExitQuery);
	
		_gtaExitWatcher.EventArrived += exitWatch_EventArrived;
		_gtaExitWatcher.Start();
	}

	private static void startWatch_EventArrived(object _, EventArrivedEventArgs e)
	{
		var processId = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);
		var process = Process.GetProcessById(processId);
		var processName = process.ProcessName;
		if (processName != "GTA5") return;
		
		SuspendProcess(process.Id);
		Console.ForegroundColor = ConsoleColor.Green;
		Console.WriteLine($">> {Language["attached-to-gta5"]}");
		Console.ResetColor();
		
		CopyReduxFilesWithExplorer();
		// await CopyReduxFilesWithTeraCopy();
		
		Console.ForegroundColor = ConsoleColor.Green;
		Console.WriteLine($"<< {Language["detached-from-gta5"]}");
		Console.ResetColor();
		
		ResumeProcess(process.Id);
		_gtaStartWatcher.Stop();
		
		Console.ForegroundColor = ConsoleColor.Green;
		Console.WriteLine($"<< {Language["waiting-for-gta5-to-close"]}");
		Console.ResetColor();
		
		StartExitWatcher();
	}

	private static void exitWatch_EventArrived(object _, EventArgs e)
	{
		Console.Clear();
		
		Console.ForegroundColor = ConsoleColor.DarkCyan;
		Console.WriteLine($">> {Language["gta5-has-been-closed"]}");
		Console.WriteLine($">> {Language["returning-to-normal-state"]}");
		
		_gtaExitWatcher.Stop();
		StartGtaWatcher();
		
		Console.WriteLine($">> {Language["returned-to-normal-state"]}");
		Console.ResetColor();
	}

	private static void CopyReduxFilesWithExplorer()
	{
		_progressBar = new ProgressBar(
			50, 
			Language["copying-redux-files"]);
		
		_progressBar.Done += () =>
		{
			Console.CursorLeft = 0;
			
			Console.ForegroundColor = ConsoleColor.Green;
			var message = $">> {Language["redux-files-copied-successfully"]}";
			Console.Write(message + new string(' ', Console.WindowWidth - message.Length));
			Console.ResetColor();
		};
		
		var files = new List<string>
		{
			FixPath(Path.Combine(_reduxPath, "update")),
			FixPath(Path.Combine(_reduxPath, "x64"))
		};
		
		var status = ExplorerFileCopier.Copy(files, _gtaVPath);
		if (!status)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($">> {Language["failed-to-copy-redux-files-to-gta5"]}");
			Console.ResetColor();
			
			Thread.Sleep(5000);
			Environment.Exit(1);
			return;
		}
		
		_progressBar.Add(50);
		
		files = new List<string> { FixPath(Path.Combine(_reduxPath, "update", "update.rpf")) };
		status = ExplorerFileCopier.Copy(files, AltVBackupDir);
		if (!status)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($">> {Language["failed-to-copy-redux-files-to-altv-backup"]}");
			Console.ResetColor();
			
			Thread.Sleep(5000);
			Environment.Exit(1);
			return;
		}
		
		_progressBar.Add(50);
		_progressBar = null;
	}

	private static async Task CopyReduxFilesWithTeraCopy()
	{
		_progressBar = new ProgressBar(
			50, 
			Language["copying-redux-files-to-gta5"]);
		
		_progressBar.Done += () =>
		{
			Console.CursorLeft = 0;
			
			Console.ForegroundColor = ConsoleColor.Green;
			var message = $">> {Language["redux-files-copied-successfully"]}";
			Console.Write(message + new string(' ', Console.WindowWidth - message.Length));
			Console.ResetColor();
		};

		var fileList = new StringBuilder()
			.AppendLine(FixPath(Path.Combine(_reduxPath, "update")))
			.AppendLine(FixPath(Path.Combine(_reduxPath, "x64")));
		
		File.WriteAllText($"{ExecutableDir}/filelist.txt", fileList.ToString().TrimEnd());
		
		await RunTeraCopySequentialAsync(_teraCopyPath,
			@"Copy *""{0}"" ""{1}"" /OverwriteAll /Close"
				.Replace("{0}", $"{ExecutableDir}/filelist.txt")
				.Replace("{1}", _gtaVPath),
			() => _progressBar.Add(50));
		
		fileList = new StringBuilder().AppendLine(FixPath(Path.Combine(_reduxPath, "update", "update.rpf")));
		File.WriteAllText($"{ExecutableDir}/filelist.txt", fileList.ToString().TrimEnd());
		
		await RunTeraCopySequentialAsync(_teraCopyPath,
			@"Copy *""{0}"" ""{1}"" /OverwriteAll /Close"
				.Replace("{0}", $"{ExecutableDir}/filelist.txt")
				.Replace("{1}", AltVBackupDir),
			() => _progressBar.Add(50));
		
		File.Delete($"{ExecutableDir}/filelist.txt");
		_progressBar = null;
	}

	private static async Task RunTeraCopySequentialAsync(string exePath, string arguments, Action onCompletion)
	{
		_teraCopyExitTcs = new TaskCompletionSource<bool>();
		StartExitTeraCopyWatcher();
		
		var teraCopyProcess = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = exePath,
				Arguments = arguments,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			}
		};
		
		teraCopyProcess.Start();
		
		await _teraCopyExitTcs.Task;
		onCompletion();
	}
	
	private static void teraCopyExit_EventArrived(object _, EventArgs e)
	{
		_teraCopyExitWatcher.Stop();
		_teraCopyExitTcs.TrySetResult(true);
	}

	private static void SuspendProcess(int pid)
	{
		try
		{
			var process = Process.GetProcessById(pid);
			foreach (ProcessThread pT in process.Threads)
			{
				var pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);
				if (pOpenThread == IntPtr.Zero) continue;
	
				SuspendThread(pOpenThread);
				CloseHandle(pOpenThread);
			}
		}
		catch { /* ignored */ }
	}
	
	private static void ResumeProcess(int pid)
	{
		try
		{
			var process = Process.GetProcessById(pid);
			if (process.ProcessName == string.Empty) return;
	
			foreach (ProcessThread pT in process.Threads)
			{
				var pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);
				if (pOpenThread == IntPtr.Zero) continue;
	
				int suspendCount;
				do
				{
					suspendCount = ResumeThread(pOpenThread);
				} while (suspendCount > 0);
	
				CloseHandle(pOpenThread);
			}
		}
		catch { /* ignored */ }
	}
	
	private static string FixPath(string path)
	{
		return path.Replace("/", "\\");
	}

	private static string _gtaVPath;
	private static string _reduxPath;
	private static string _teraCopyPath;
	
	private const string Version = "0.0.4";
	private const string ProgramName = "ReduxShield";
	private const string ConfigFileName = "config.ini";
	
	private static ProgressBar _progressBar;
	private static ManagementEventWatcher _gtaExitWatcher;
	private static ManagementEventWatcher _gtaStartWatcher;
	private static ManagementEventWatcher _teraCopyExitWatcher;
	private static TaskCompletionSource<bool> _teraCopyExitTcs;
	
	private static readonly string ExecutablePath = Process.GetCurrentProcess().MainModule?.FileName;
	private static readonly string ExecutableDir = Path.GetDirectoryName(ExecutablePath);
	private static readonly string SystemUserName = Environment.UserName;
	private static readonly string SystemDrive = Path.GetPathRoot(Environment.SystemDirectory);
	private static readonly string AltVBackupDir = Path.Combine(
		SystemDrive, "Users", SystemUserName, "AppData", "Local", "altv-majestic", "backup");
	private static readonly string SystemLanguage = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.ToUpper();
	private static readonly Dictionary<string, Dictionary<string, string>> Localizations = new()
	{
		{
			"RU", new Dictionary<string, string>
			{
				{ "supported-langs", "Поддерживаемые языки" },
				{ "redux-files-path", "Путь с файлами редукса" },
				{ "gta-files-path", "Путь к GTA5" },
				{ "start-message", "Redux Shield v{0} запущен." },
				{ "restart-with-admin", "Запущено без прав администратора. Запрос на повторный запуск с правами админа..." },
				{ "failed-to-start", "Не удалось запустить с правами администратора. Завершение работы..." },
				{ "please-run-as-admin", "Пожалуйста, запустите программу с правами администратора или подтвердите запрос в следующий раз." },
				{ "config-file-not-found", "Файл конфигурации не найден. Создание файла конфигурации по умолчанию." },
				{ "unable-to-get-executable-directory", "Не удалось получить директорию исполняемого файла." },
				{ "invalid-config-file", "Неверный файл конфигурации. Пожалуйста, заполните обязательные поля." },
				{ "unknown-config-key", "Неизвестный ключ конфигурации: \"{0}\" > Игнорирование..." },
				{ "invalid-paths-in-config-file", "Неверные пути в файле конфигурации. Пожалуйста, проверьте пути." },
				{ "altv-backup-dir-not-found", "Директория резервного копирования alt:V не найдена. Пожалуйста, проверьте путь." },
				{ "attached-to-gta5", "Выполнен перехват GTA5." },
				{ "detached-from-gta5", "Процесс GTA5 возобновлен. GTA5 теперь работает с файлами Redux." },
				{ "waiting-for-gta5-to-close", "Ожидание закрытия GTA5 для возврата в нормальное состояние..." },
				{ "gta5-has-been-closed", "GTA5 была закрыт." },
				{ "returning-to-normal-state", "Возвращение в нормальное состояние..." },
				{ "returned-to-normal-state", "Возвращено в нормальное состояние. Ожидание след.запуска GTA5..." },
				{ "copying-redux-files", "Копирование файлов Redux в директорию GTA5 и директорию резервного копирования alt:V." },
				{ "copying-redux-files-to-gta5", "Копирование файлов Redux в директорию GTA5 и директорию резервного копирования alt:V." },
				{ "failed-to-copy-redux-files-to-gta5", "Не удалось скопировать файлы Redux в директорию GTA5." },
				{ "failed-to-copy-redux-files-to-altv-backup", "Не удалось скопировать файлы Redux в директорию резервного копирования alt:V." },
				{ "redux-files-copied-successfully", "Файлы Redux успешно скопированы." }
			}
		},
		{
			"EN", new Dictionary<string, string>
			{
				{ "supported-langs", "Supported languages" },
				{ "redux-files-path", "Path to Redux files" },
				{ "gta-files-path", "Path to GTA5" },
				{ "start-message", "Redux Shield v{0} is started." },
				{ "restart-with-admin", "Running without admin rights. Requesting to restart with admin rights..." },
				{ "failed-to-start", "Failed to start with admin rights. Exiting..." },
				{ "please-run-as-admin", "Please run the program as an administrator or confirm the request next time." },
				{ "unable-to-get-executable-directory", "Unable to get the executable directory." },
				{ "config-file-not-found", "Config file not found. Create default config file." },
				{ "unknown-config-key", "Unknown config key: \"{0}\" > Ignoring..." },
				{ "invalid-config-file", "Invalid config file. Please fill in the required fields." },
				{ "invalid-paths-in-config-file", "Invalid paths in config file. Please check the paths." },
				{ "altv-backup-dir-not-found", "alt:V backup directory not found. Please check the path." },
				{ "attached-to-gta5", "Attached to GTA5." },
				{ "detached-from-gta5", "Detached from GTA5. GTA5 is now running with Redux files." },
				{ "waiting-for-gta5-to-close", "Waiting for GTA5 to close to return to normal state..." },
				{ "gta5-has-been-closed", "GTA5 has been closed." },
				{ "returning-to-normal-state", "Returning to normal state..." },
				{ "returned-to-normal-state", "Returned to normal state. Waiting for GTA5 to start again..." },
				{ "copying-redux-files", "Copying Redux files to GTA5 directory and alt:V backup directory." },
				{ "failed-to-copy-redux-files-to-gta5", "Failed to copy Redux files to GTA5 directory." },
				{ "failed-to-copy-redux-files-to-altv-backup", "Failed to copy Redux files to alt:V backup directory." },
				{ "copying-redux-files-to-gta5", "Copying Redux files to GTA5 directory and alt:V backup directory." },
				{ "redux-files-copied-successfully", "Redux files copied successfully." }
			}
		}
	};

	private static readonly Dictionary<string, string> Language = Localizations[SystemLanguage];
	private static readonly StringBuilder DefaultConfig = new StringBuilder()
		.AppendLine($"// {Language["supported-langs"]}: RU / EN")
		.AppendLine($"// {Language["redux-files-path"]}")
		.AppendLine($"ReduxPath={Path.Combine(SystemDrive, "Users", SystemUserName, "Downloads", "Redux")}")
		.AppendLine()
		.AppendLine($"// {Language["gta-files-path"]}")
		.AppendLine("GtaVPath=D:/Games/Grand Theft Auto V");
}