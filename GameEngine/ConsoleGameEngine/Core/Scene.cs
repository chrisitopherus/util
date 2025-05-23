﻿using ConsoleGameEngine.Interfaces;
using Helpers.Utility.Keyboard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleGameEngine.Core;

public abstract class Scene : IInitializable, IRenderable, ISpawner<GameEntity>, IDespawner<GameEntity>
{
    protected readonly List<GameEntity> gameEntities = [];
    protected readonly Queue<GameEntity> spawnQueue = [];
    protected readonly Queue<GameEntity> despawnQueue = [];
    protected readonly Queue<GameEntity> unrenderQueue = [];

    public IEnumerable<GameEntity> Entities
    {
        get
        {
            return this.gameEntities;
        }
    }

    public bool BlocksUpdate { get; protected set; } = true;

    public virtual bool BlocksRender { get; protected set; } = true;

    public virtual bool BlocksInput { get; protected set; } = true;

    public bool IsInitialized
    {
        get;
        private set;
    }

    public virtual void Spawn(GameEntity entity)
    {
        this.spawnQueue.Enqueue(entity);
    }

    public virtual void Despawn(GameEntity entity)
    {
        this.despawnQueue.Enqueue(entity);
    }

    public virtual void Update(TimeSpan deltaTime)
    {
        this.ApplySpawn();
        foreach (GameEntity entity in this.Entities)
        {
            entity.TryUpdate(deltaTime);
        }

        this.CheckForDespawnRequests(this.gameEntities);
        this.ApplyDespawn();
    }

    public virtual void HandleKeyInput(ConsoleKeyData keyData)
    {
        foreach (GameEntity entity in this.Entities)
        {
            entity.HandleKeyInput(keyData);
        }
    }

    public virtual void Render()
    {
        this.PreRender();
        GameEntity[] dirtyEntities = this.gameEntities.Where(entity => entity.IsDirty).ToArray();
        this.RenderScene(dirtyEntities);
        foreach (GameEntity entity in dirtyEntities)
        {
            entity.ClearDirty();
        }
    }

    public void Initialize()
    {
        this.Init();
        this.IsInitialized = true;
    }

    protected abstract void Init();

    protected abstract void RenderScene(IEnumerable<GameEntity> entitiesToRender);

    protected virtual void HandleSpawn(GameEntity entity)
    {
        entity.Scene = this;
        entity.OnSpawn();
        this.gameEntities.Add(entity);
    }

    protected virtual void HandleDespawn(GameEntity entity)
    {
        entity.Scene = default;
        entity.OnDespawn();
        this.gameEntities.Remove(entity);
    }

    protected virtual void PreRender()
    {
        while (this.unrenderQueue.Count > 0)
        {
            GameEntity entity = this.unrenderQueue.Dequeue();
            this.UnrenderEntity(entity);
        }
    }

    protected virtual void UnrenderEntity(GameEntity entity)
    {
        // undrawing sprite
    }

    private void CheckForDespawnRequests(IEnumerable<GameEntity> entities)
    {
        foreach (GameEntity entity in entities.Where(e => e.IsDespawnRequested))
        {
            this.Despawn(entity);
        }
    }

    private void ApplySpawn()
    {
        while (this.spawnQueue.Count > 0)
        {
            GameEntity entity = this.spawnQueue.Dequeue();
            this.HandleSpawn(entity);
        }
    }

    private void ApplyDespawn()
    {
        while (this.despawnQueue.Count > 0)
        {
            GameEntity entity = this.despawnQueue.Dequeue();
            this.unrenderQueue.Enqueue(entity);
            this.HandleDespawn(entity);
        }
    }
}
