﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Network.Architecture;

namespace Network.Architecture.Interfaces;

public interface ILifecycleComponent
{
    event EventHandler? Started;
    event EventHandler? Stopped;

    LifecycleState State { get; }
    void Start();
    void Stop();
}
