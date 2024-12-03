using System;

namespace ReduxShield;

public sealed class ProgressBar
{
	public ProgressBar(int totalBars = 50, string description = "")
	{
		_totalBars = totalBars;
		_currentPercentage = 0;
		_description = description;
		Draw();
	}

	private void Draw()
	{
		Console.Write($"[{new string('-', _totalBars)}] 0% | {_description}");
	}
	
	public void Add(int percentage)
	{
		Update(_currentPercentage + percentage);
	}

	private void Update(int percentage)
	{
		percentage = percentage switch
		{
			< 0 => 0,
			> 100 => 100,
			_ => percentage
		};

		if (_currentPercentage == percentage) return;
		_currentPercentage = percentage;

		Console.CursorLeft = 0;
		
		var filledBars = percentage * _totalBars / 100;

		var progress = new string('#', filledBars) + new string('-', _totalBars - filledBars);
		Console.Write($"[{progress}] {percentage}% | {_description}");
		
		if (percentage == 100) OnDone();
	}
	
	public event Action Done;
	private void OnDone() => Done?.Invoke();
	
	private int _currentPercentage;
	private readonly int _totalBars;
	private readonly string _description;
}
