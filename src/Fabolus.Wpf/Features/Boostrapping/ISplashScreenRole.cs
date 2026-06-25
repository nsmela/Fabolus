namespace Fabolus.Wpf.Features.Bootstrapping;

public interface ISplashScreenRole {
    void Reveal();
    void Conceal();
}

public interface IMainApplicationRole {
    void Launch();
}