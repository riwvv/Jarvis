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
}
