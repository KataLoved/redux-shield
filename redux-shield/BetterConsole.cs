using System;

namespace ReduxShield;

public static class BetterConsole
{
	public static void Write(string message, ConsoleColor color = ConsoleColor.White, int delay = 0)
	{
		if (color != ConsoleColor.White) Console.ForegroundColor = color;
		if (delay == 0) Console.Write(message);
		else foreach (var c in message)
		{
			Console.Write(c);
			System.Threading.Thread.Sleep(delay);
		}
		
		Console.ResetColor();
	}
	
	public static void WriteLine(string message, ConsoleColor color = ConsoleColor.White, int delay = 0)
		=> Write($"{message + Environment.NewLine}", color, delay);
	
	public static void WriteError(string message, int delay = 0)
		=> WriteLine(message, ConsoleColor.Red, delay);
	
	public static void WriteWarning(string message, int delay = 0)
		=> WriteLine(message, ConsoleColor.Yellow, delay);
}