using Microsoft.SemanticKernel;

namespace Jarvis.Interfaces;

public interface IKernelService {
    void SetKernel(Kernel kernel);
    Kernel? GetKernel();
}
