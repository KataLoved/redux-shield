using System;

namespace ReduxShield;

internal class Program
{
	public static void Main(string[] args)
	{
		Console.SetWindowSize(125, 30);
		
		var bootstrap = new Bootstrapper();
		bootstrap.Initialize().Wait();
	}
}