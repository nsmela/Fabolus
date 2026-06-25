
namespace Fabolus.Core.Features.Smoothing;

public record SmoothSettings(int Iterations = 1, float Intensity = 1.0f, float Inflation = 0.1f, float RemeshRatio = 2.0f, float Resolution = 1.0f);
