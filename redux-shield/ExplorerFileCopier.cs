using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;

namespace ReduxShield;

public static class ExplorerFileCopier
{
	public static bool Copy(List<string> sourcePaths, string destinationPath)
	{
		var invalidPaths = sourcePaths.Where(path => !File.Exists(path) && !Directory.Exists(path)).ToList();
		if (invalidPaths.Count > 0)
		{
			throw new ArgumentException($"Неверные пути: {string.Join(", ", invalidPaths)}");
		}
		
		var invalidDestinationPath = !Directory.Exists(destinationPath);
		if (invalidDestinationPath)
		{
			throw new ArgumentException($"Неверный путь назначения: {destinationPath}");
		}
		
		var sourceFiles = string.Join("\0", sourcePaths) + "\0";
		var fileOp = new SHFILEOPSTRUCT
		{
			wFunc = FO_COPY,
			pFrom = sourceFiles, 
			pTo = destinationPath + "\0",
			fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION
		};
		
		return SHFileOperation(ref fileOp) == 0;
	}
	
	[DllImport("shell32.dll", CharSet = CharSet.Auto)]
	private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);
	
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct SHFILEOPSTRUCT
	{
		public IntPtr hwnd;
		public uint wFunc;
		public string pFrom;
		public string pTo;
		public ushort fFlags;
		public bool fAnyOperationsAborted;
		public IntPtr hNameMappings;
		public string lpszProgressTitle;
	}

	private const uint FO_COPY = 0x0002;
	private const ushort FOF_ALLOWUNDO = 0x0040;
	private const ushort FOF_NOCONFIRMATION = 0x0010;
}