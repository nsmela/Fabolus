﻿using Fabolus.Core.AirChannel;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Common.Extensions;
public static class EnumExtensions {
    /// <summary>
    /// Returns the Description attribute of the Enum or the Enum's name
    /// </summary>
    /// <param name="value"></param>
    /// <returns>Description attribute to string, or Enum's name</returns>
    public static string GetDescriptionString(this Enum value) {
        var attribute = value
            .GetType()
            .GetField(value.ToString())
            ?.GetCustomAttributes(typeof(DescriptionAttribute), false)
            .SingleOrDefault() as DescriptionAttribute;

        return attribute is null
            ? value.ToString()
            : attribute.Description;
    }

}