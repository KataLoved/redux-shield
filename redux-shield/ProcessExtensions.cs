using System.Diagnostics;

namespace ReduxShield;

public static class ProcessExtensions
{
	private static string FindIndexedProcessName(int pid) {
		var processName = Process.GetProcessById(pid).ProcessName;
		var processesByName = Process.GetProcessesByName(processName);
		string processIndexedName = null;

		for (var index = 0; index < processesByName.Length; index++) {
			processIndexedName = index == 0 ? processName : processName + "#" + index;
			var processId = new PerformanceCounter("Process", "ID Process", processIndexedName);
			if ((int) processId.NextValue() == pid) {
				return processIndexedName;
			}
		}

		return processIndexedName;
	}

	private static Process FindPidFromIndexedProcessName(string indexedProcessName) {
		var parentId = new PerformanceCounter("Process", "Creating Process ID", indexedProcessName);
		return Process.GetProcessById((int) parentId.NextValue());
	}

	public static Process Parent(this Process process) {
		return FindPidFromIndexedProcessName(FindIndexedProcessName(process.Id));
	}
}