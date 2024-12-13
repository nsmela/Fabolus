using Fabolus.Wpf.Common.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Common.Helpers;
public static class EnumHelper {
    /// <summary>
    /// Returns a list of all enums of the Enum type
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns>List of Enum belonging to the type</returns>
    public static IEnumerable<T> GetEnums<T>() where T : Enum
        => Enum.GetValues(typeof(T)).Cast<T>();

    /// <summary>
    /// Returns an array of names for each Enum belonging to the enum type
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static IEnumerable<string> GetEnumDescriptions<T>() where T : Enum
        => GetEnums<T>().Select(x => x.GetDescriptionString());
}
