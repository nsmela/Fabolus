using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes;

public record struct Result {

    public DMesh3 Mesh { get; set; }

    public bool IsSuccess { get; set; }
    public bool IsFailure => !IsSuccess;

    public List<MeshError> Errors { get; set; }

    public Result(DMesh3 mesh) {
        Mesh = mesh;
        IsSuccess = true;
        Errors = new List<MeshError> { MeshError.NONE };
    }

    public static Result Pass(DMesh3 mesh) => new Result(mesh);
    public static Result Fail(List<MeshError> errors) => new Result() { IsSuccess = false, Errors = errors };
}

public record struct Result<T> {
    public T? Data { get; set; }
    public bool IsSuccess { get; set; }
    public bool IsFailure => !IsSuccess;
    public List<MeshError> Errors { get; set; }
    public Result(T mesh) {
        Data = mesh;
        IsSuccess = true;
        Errors = new List<MeshError> { MeshError.NONE };
    }

    public static Result<T> Pass(T mesh) => new Result<T>(mesh);
    public static Result<T> Fail(List<MeshError> errors) => new Result<T>() { IsSuccess = false, Errors = errors };
    public static Result<T> Fail(MeshError error) => new Result<T>() { IsSuccess = false, Errors = [error] };

    public static implicit operator Result<T>(T data) => new Result<T> { Data = data };
    public static implicit operator Result<T>(List<MeshError> errors) => new Result<T> { IsSuccess = false, Errors = errors };
}

public record struct MeshError {
    public static readonly MeshError NONE = new("");

    public string ErrorMessage { get; set; } = string.Empty;

    public MeshError(string errorMessage) {
        ErrorMessage = errorMessage;
    }
};