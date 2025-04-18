﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CLI.Interfaces
{
    public interface IModifier : IHasIdentifiers
    {
        string Description { get; }
        bool IsFlag { get; set; }
        bool IsRequired { get; set; }
    }
}