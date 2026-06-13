using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace Jarvis.Plugins;

public class MiniGamePlugin {
    [KernelFunction]
    [Description("Подбрасываем кость/кубик что бы получить случайное число от 1 до 6")]
    public async Task<string> RollTheDice() {
		try {
            var number = new Random().Next(1, 7);
            return $"Выпало: {number}";
		}
		catch (Exception) {
            return "Кубик улетел слишком далеко";
		}
    }

    [KernelFunction]
    [Description("Подбрасываем монетку что бы получить 'орёл' или 'решка'")]
    public async Task<string> FlipCoin() {
        try {
            var number = new Random().Next(1, 3);
            return number switch {
                1 => "Орёл",
                2 => "Решка",
                _ => "Ой, встала на ребро"
            };
        }
        catch (Exception) {
            return "Ой, встала на ребро";
        }
    }

    [KernelFunction]
    [Description("Случайное число от 1 до 10")]
    public async Task<string> RandomNumberToTen() => $"Случайное число: {new Random().Next(1, 11)}";

    [KernelFunction]
    [Description("Случайное число от 1 до 100")]
    public async Task<string> RandomNumberToHundred() => $"Случайное число: {new Random().Next(1, 101)}";

    [KernelFunction]
    [Description("Случайное число от 1 до число которое скажет пользователь")]
    public async Task<string> RandomNumber([Description("Максимальное число")] int maxValue) {
        try {
            if (maxValue <= 1)
                throw new Exception("Укажите правильный диапазон");
            return $"Случайное число: {new Random().Next(1, maxValue + 1)}";
        }
        catch (Exception ex) {
            return $"{ex.Message}. Но я придумал {new Random().Next(1, 11)}";
        }
    }

    [KernelFunction]
    [Description("Угадай число от 1 до 10")]
    public async Task<string> GuessNumber([Description("Число, которое предположил пользователь")] int guess) {
        try {
            var secret = new Random().Next(1, 11);
            if (guess == secret)
                return "Поздравляю! Угадал";
            return "Не угадал";
        }
        catch (Exception) {
            return "Не угадал";
        }
    }
}
