﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Helpers.Utility;

public class ConsoleStyle
{
    public ConsoleStyle(ConsoleColor foreground, ConsoleColor background)
    {
        this.Foreground = foreground;
        this.Background = background;
    }

    public ConsoleColor Foreground { get; init; }
    public ConsoleColor Background { get; init; }

    public void Apply()
    {
        Console.ForegroundColor = this.Foreground;
        Console.BackgroundColor = this.Background;
    }
}
