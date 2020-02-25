﻿using System;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.API.Common
{
    public delegate bool CanSpawnNearbyDelegate(EntityProperties type, Vec3d spawnPosition, RuntimeSpawnConditions sc);

    public class EntityPlayer : EntityHumanoid
    {
        /// <summary>
        /// The block or blocks currently selected by the player.
        /// </summary>
        public BlockSelection BlockSelection;

        /// <summary>
        /// The entity or entities selected by the player.
        /// </summary>
        public EntitySelection EntitySelection;

        /// <summary>
        /// The reason the player died (if the player did die)
        /// </summary>
        public DamageSource DeathReason;

        /// <summary>
        /// The camera position of the player's view.
        /// </summary>
        public Vec3d CameraPos = new Vec3d();

        /// <summary>
        /// The yaw the player currently wants to walk towards to. Value set by the PlayerPhysics system.
        /// </summary>
        public float WalkYaw;
        /// <summary>
        /// The pitch the player currently wants to move to. Only relevant while swimming. Value set by the PlayerPhysics system.
        /// </summary>
        public float WalkPitch;

        /// <summary>
        /// The current height of the eyes.
        /// </summary>
        double eyeHeightCurrent;

        /// <summary>
        /// Set this to hook into the foot step sound creator thingy. Currently used by the armor system to create armor step sounds 
        /// </summary>
        public Action OnFootStep;
        public Action OnImpact;
        public CanSpawnNearbyDelegate OnCanSpawnNearby;

        public override bool StoreWithChunk
        {
            get { return false; }

        }

        /// <summary>
        /// The player's internal Universal ID
        /// </summary>
        public string PlayerUID
        {
            get { return WatchedAttributes.GetString("playerUID"); }
        }

        public override ItemSlot RightHandItemSlot
        {
            get
            {
                IPlayer player = World.PlayerByUid(PlayerUID);
                return player?.InventoryManager.ActiveHotbarSlot;
            }
        }

        public override ItemSlot LeftHandItemSlot
        {
            get
            {
                IPlayer player = World.PlayerByUid(PlayerUID);
                return player?.InventoryManager?.GetHotbarInventory()?[10];
            }
        }

        public override IInventory GearInventory
        {
            get
            {
                IPlayer player = World.PlayerByUid(PlayerUID);
                return player?.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
            }
        }


        public override byte[] LightHsv
        {
            get {
                byte[] rightHsv = RightHandItemSlot?.Itemstack?.Block?.GetLightHsv(World.BlockAccessor, null, RightHandItemSlot.Itemstack);
                byte[] leftHsv = LeftHandItemSlot?.Itemstack?.Block?.GetLightHsv(World.BlockAccessor, null, LeftHandItemSlot.Itemstack);

                if (rightHsv == null) return leftHsv;
                if (leftHsv == null) return rightHsv;

                float totalval = rightHsv[2] + leftHsv[2];
                float t = leftHsv[2] / totalval;

                return new byte[]
                {
                    (byte)(leftHsv[0] * t + rightHsv[0] * (1-t)),
                    (byte)(leftHsv[1] * t + rightHsv[1] * (1-t)),
                    Math.Max(leftHsv[2], rightHsv[2])
                };
            }
        }

        public override bool AlwaysActive
        {
            get { return true; }
        }

        public override bool ShouldDespawn
        {
            get { return false; }
        }

        internal override bool LoadControlsFromServer
        {
            get
            {
                return !(World is IClientWorldAccessor) || ((IClientWorldAccessor)World).Player.Entity.EntityId != EntityId;
            }
        }

        public override bool IsInteractable
        {
            get
            {
                IWorldPlayerData worldData = World?.PlayerByUid(PlayerUID)?.WorldData;
                return worldData?.CurrentGameMode != EnumGameMode.Spectator && worldData?.NoClip != true;
            }
        }

        /// <summary>
        /// The base player attached to this EntityPlayer.
        /// </summary>
        public IPlayer Player
        {
            get
            {
                return World?.PlayerByUid(PlayerUID);
            }
        }


        public EntityPlayer() : base()
        {

        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long chunkindex3d)
        {
            base.Initialize(properties, api, chunkindex3d);
        }


        public override double EyeHeight
        {
            get
            {
                if (MountedOn?.SuggestedAnimation == "sleep")
                {
                    return 0.3;
                }

                return eyeHeightCurrent;
            }
        }

        double walkCounter;
        double prevStepHeight;
        int direction = 0;

        bool lastCanStandUp;
        public ClimateCondition selfClimateCond;
        float climateCondAccum;

        public override double GetWalkSpeedMultiplier(double groundDragFactor = 0.3)
        {
            double mul = base.GetWalkSpeedMultiplier(groundDragFactor);

            if (!servercontrols.Sneak && !lastCanStandUp)
            {
                mul *= GlobalConstants.SneakSpeedMultiplier;
            }

            return mul;
        }


        public override void OnGameTick(float dt)
        {
            IPlayer player = World.PlayerByUid(PlayerUID);

            if (player?.WorldData?.CurrentGameMode != EnumGameMode.Spectator)
            {
                lastCanStandUp = !servercontrols.Sneak && canStandUp();

                double newEyeheight = Properties.EyeHeight;
                double newModelHeight = Properties.HitBoxSize.Y;

                if (servercontrols.FloorSitting)
                {
                    newEyeheight *= 0.5f;
                    newModelHeight *= 0.55f;
                }
                else if ((servercontrols.Sneak || !lastCanStandUp) && !servercontrols.IsClimbing && !servercontrols.IsFlying)
                {
                    newEyeheight *= 0.8f;
                    newModelHeight *= 0.8f;
                } else if (!Alive)
                {
                    newEyeheight *= 0.25f;
                    newModelHeight *= 0.25f;
                }

                bool moving = (servercontrols.TriesToMove && LocalPos.Motion.LengthSq() > 0.00001) && !servercontrols.NoClip && !servercontrols.FlyMode && OnGround;

                double frequency = dt * servercontrols.MovespeedMultiplier * GetWalkSpeedMultiplier(0.3) * (servercontrols.Sprint ? 0.9 : 1.2);

                walkCounter = moving ? walkCounter + frequency : 0;
                walkCounter = walkCounter % GameMath.TWOPI;

                double sneakMul = (servercontrols.Sneak ? 1.7 : 1);

                double amplitude = (FeetInLiquid ? 0.8 : 1) / (3 * sneakMul) + (servercontrols.Sprint ? 0.2 : 0);
                double offset = -0.2 / sneakMul;


                double stepHeight = -Math.Max(0, Math.Abs(GameMath.Sin(5.5f * walkCounter) * amplitude) + offset);
                if (World.Side == EnumAppSide.Client && (World.Api as ICoreClientAPI).Settings.Bool["viewBobbing"]) newEyeheight += stepHeight;



                if (moving)
                {
                    if (stepHeight > prevStepHeight)
                    {
                        if (direction == -1)
                        {

                            float volume = controls.Sneak ? 0.5f : 1f;

                            int blockIdUnder = BlockUnderPlayer();
                            int blockIdInside = BlockInsidePlayer();

                            AssetLocation soundwalk = World.Blocks[blockIdUnder].Sounds?.Walk;
                            AssetLocation soundinside = World.Blocks[blockIdInside].Sounds?.Inside;

                            if (!Swimming && soundwalk != null)
                            {
                                if (blockIdInside != blockIdUnder && soundinside != null)
                                {
                                    World.PlaySoundAt(soundwalk, this, player, true, 12, volume * 0.5f);
                                    World.PlaySoundAt(soundinside, this, player, true, 12, volume);
                                }
                                else
                                {
                                    World.PlaySoundAt(soundwalk, this, player, true, 12, volume);
                                }

                                OnFootStep?.Invoke();
                            }

                        }
                        direction = 1;
                    }
                    else
                    {
                        direction = -1;
                        
                    }

                }

                prevStepHeight = stepHeight;

                double diff = (newEyeheight - eyeHeightCurrent) * 5 * dt;
                eyeHeightCurrent = diff > 0 ? Math.Min(eyeHeightCurrent + diff, newEyeheight) : Math.Max(eyeHeightCurrent + diff, newEyeheight);

                diff = (newModelHeight - OriginCollisionBox.Y2) * 5 * dt;
                OriginCollisionBox.Y2 = CollisionBox.Y2 = (float)(diff > 0 ? Math.Min(CollisionBox.Y2 + diff, newModelHeight) : Math.Max(CollisionBox.Y2 + diff, newModelHeight));
            }

            climateCondAccum += dt;
            if (World.Side == EnumAppSide.Client && climateCondAccum > 0.5f)
            {
                climateCondAccum = 0;
                selfClimateCond = Api.World.BlockAccessor.GetClimateAt(Pos.XYZ.AsBlockPos);
            }

            if (!servercontrols.Sneak && !lastCanStandUp)
            {
                // So the sneak animation plays still
                servercontrols.Sneak = true;
                base.OnGameTick(dt);
                servercontrols.Sneak = false;
            } else
            {
                base.OnGameTick(dt);
            }
        }


        Cuboidf tmpCollBox = new Cuboidf();
        private bool canStandUp()
        {
            tmpCollBox.Set(CollisionBox);
            tmpCollBox.Y2 = Properties.HitBoxSize.Y;
            tmpCollBox.Y1 += 1f; // Don't care about the bottom block
            bool collide = World.CollisionTester.IsColliding(World.BlockAccessor, tmpCollBox, Pos.XYZ, false); ;
            return !collide;
        }

        public virtual bool CanSpawnNearby(EntityProperties type, Vec3d spawnPosition, RuntimeSpawnConditions sc)
        {
            if (OnCanSpawnNearby != null)
            {
                return OnCanSpawnNearby(type, spawnPosition, sc);
            }

            return true;
        }

        public override void OnFallToGround(double motionY)
        {
            IPlayer player = World.PlayerByUid(PlayerUID);

            if (player?.WorldData?.CurrentGameMode != EnumGameMode.Spectator)
            {
                int blockIdUnder = BlockUnderPlayer();
                AssetLocation soundwalk = World.Blocks[blockIdUnder].Sounds?.Walk;
                if (soundwalk != null && !Swimming)
                {
                    World.PlaySoundAt(soundwalk, this, player, true, 12, 1.5f);
                }

                OnImpact?.Invoke();
            }

            base.OnFallToGround(motionY);
        }



        internal int BlockUnderPlayer()
        {
            EntityPos pos = LocalPos;
            return World.BlockAccessor.GetBlockId(
                (int)pos.X,
                (int)(pos.Y - 0.1f),
                (int)pos.Z);
        }

        internal int BlockInsidePlayer()
        {
            EntityPos pos = LocalPos;

            return World.BlockAccessor.GetBlockId(
                (int)pos.X,
                (int)(pos.Y + 0.1f),
                (int)pos.Z);
        }




        public override bool TryGiveItemStack(ItemStack itemstack)
        {
            IPlayer player = World.PlayerByUid(PlayerUID);
            if (player != null) return player.InventoryManager.TryGiveItemstack(itemstack, true);
            return false;
        }


        public override void Die(EnumDespawnReason reason = EnumDespawnReason.Death, DamageSource damageSourceForDeath = null)
        {
            base.Die(reason, damageSourceForDeath);

            DeathReason = damageSourceForDeath;
            DespawnReason = null;
            DeadNotify = true;
            TryStopHandAction(true, EnumItemUseCancelReason.Death);
            TryUnmount();

            if (Properties.Server?.Attributes?.GetBool("keepContents", false) != true)
            {
                World.PlayerByUid(PlayerUID).InventoryManager.OnDeath();
            }

            if (Properties.Server?.Attributes?.GetBool("dropArmorOnDeath", false) == true)
            {
                foreach (var slot in GearInventory)
                {
                    if (slot.Empty) continue;
                    if (slot.Itemstack.ItemAttributes?["protectionModifiers"].Exists == true)
                    {
                        Api.World.SpawnItemEntity(slot.Itemstack, ServerPos.XYZ);
                        slot.Itemstack = null;
                        slot.MarkDirty();
                    }
                }
            }
        }

        public override void Revive()
        {
            base.Revive();

            (Api as ICoreServerAPI).Network.SendEntityPacket(Api.World.PlayerByUid(PlayerUID) as IServerPlayer, this.EntityId, 196);
        }


        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (packetid == 196)
            {
                AnimManager?.StopAnimation("die");
                return;
            }
            if (packetid == 1203)
            {
                string animation = SerializerUtil.Deserialize<string>(data);
                StartAnimation(animation);
            }

            base.OnReceivedServerPacket(packetid, data);
        }

        public override bool ShouldReceiveDamage(DamageSource damageSource, float damage)
        {
            EnumGameMode mode = World.PlayerByUid(PlayerUID).WorldData.CurrentGameMode;
            if ((mode == EnumGameMode.Creative || mode == EnumGameMode.Spectator) && damageSource.Type != EnumDamageType.Heal) return false;

            return base.ShouldReceiveDamage(damageSource, damage);
        }


        public override void OnHurt(DamageSource damageSource, float damage)
        {
            if (damage > 0 && World != null && World.Side == EnumAppSide.Client && (World as IClientWorldAccessor).Player.Entity.EntityId == this.EntityId)
            {
                (World as IClientWorldAccessor).AddCameraShake(0.3f);
            }

            if (damage != 0 && World?.Side == EnumAppSide.Server)
            {
                bool heal = damageSource.Type == EnumDamageType.Heal;

                string msg = Lang.Get(heal ? "Gained {0} hp through {1}" : "Lost {0} hp through {1}", damage, damageSource.Type.ToString().ToLowerInvariant());// damageSource.source.ToString().ToLowerInvariant());

                if (damageSource.Source == EnumDamageSource.Player)
                {
                    EntityPlayer eplr = damageSource.SourceEntity as EntityPlayer;
                    msg = Lang.Get(heal ? "Gained {0} hp by player {1}" : "Lost {0} hp by player {1}", damage, damageSource.Source.ToString().ToLowerInvariant(), World.PlayerByUid(eplr.PlayerUID).PlayerName);
                }

                if (damageSource.Source == EnumDamageSource.Entity)
                {
                    string creatureName = Lang.Get("prefixandcreature-" + damageSource.SourceEntity.Code.Path.Replace("-", ""));
                    msg = Lang.Get(heal ? "Gained {0} hp by {1}" : "Lost {0} hp by {1}", damage, creatureName);
                }

                (World.PlayerByUid(PlayerUID) as IServerPlayer).SendMessage(GlobalConstants.DamageLogChatGroup, msg, EnumChatType.Notification);
            }
        }



        public override bool TryStopHandAction(bool forceStop, EnumItemUseCancelReason cancelReason = EnumItemUseCancelReason.ReleasedMouse)
        {
            if (controls.HandUse == EnumHandInteract.None || RightHandItemSlot?.Itemstack == null) return true;

            IPlayer player = World.PlayerByUid(PlayerUID);
            float secondsPassed = (World.ElapsedMilliseconds - controls.UsingBeginMS) / 1000f;

            if (forceStop)
            {
                controls.HandUse = EnumHandInteract.None;
            }
            else
            {
                controls.HandUse = RightHandItemSlot.Itemstack.Collectible.OnHeldUseCancel(secondsPassed, RightHandItemSlot, this, player.CurrentBlockSelection, player.CurrentEntitySelection, cancelReason);
            }


            if (controls.HandUse == EnumHandInteract.None)
            {
                RightHandItemSlot.Itemstack.Collectible.OnHeldUseStop(secondsPassed, RightHandItemSlot, this, player.CurrentBlockSelection, player.CurrentEntitySelection, controls.HandUse);
            }

            return controls.HandUse == EnumHandInteract.None;
        }


        public override void WalkInventory(OnInventorySlot handler)
        {
            IPlayer player = World.PlayerByUid(PlayerUID);

            foreach (InventoryBase inv in player.InventoryManager.Inventories.Values)
            {
                if (!inv.HasOpened(player)) continue;

                int q = inv.Count;
                for (int i = 0; i < q; i++)
                {
                    if (!handler(inv[i])) return;
                }
            }
        }

        /// <summary>
        /// Sets the current player.
        /// </summary>
        public void SetCurrentlyControlledPlayer()
        {
            this.servercontrols = controls;
        }

        public override void OnCollideWithLiquid()
        {
            if (World?.PlayerByUid(PlayerUID)?.WorldData != null && World.PlayerByUid(PlayerUID).WorldData.CurrentGameMode != EnumGameMode.Spectator)
            {
                base.OnCollideWithLiquid();
            }
        }


       
        

        public override void TeleportToDouble(double x, double y, double z, API.Common.Action onTeleported = null)
        {
            Teleporting = true;
            ICoreServerAPI sapi = this.World.Api as ICoreServerAPI;
            if (sapi != null)
            {
                sapi.WorldManager.LoadChunkColumnFast((int)ServerPos.X / World.BlockAccessor.ChunkSize, (int)ServerPos.Z / World.BlockAccessor.ChunkSize, new ChunkLoadOptions()
                {
                    OnLoaded = () =>
                    {
                        Pos.SetPos(x, y, z);
                        ServerPos.SetPos(x, y, z);
                        PreviousServerPos.SetPos(-99, -99, -99);
                        PositionBeforeFalling.Set(x, y, z);
                        Pos.Motion.Set(0, 0, 0);
                        if (this is EntityPlayer)
                        {
                            sapi.Network.BroadcastEntityPacket(EntityId, 1, SerializerUtil.Serialize(ServerPos.XYZ));
                            IServerPlayer player = (this as EntityPlayer).Player as IServerPlayer;
                            int chunksize = World.BlockAccessor.ChunkSize;
                            player.CurrentChunkSentRadius = 0;
                            
                            sapi.Event.RegisterCallback((bla) => {
                                sapi.WorldManager.SendChunk((int)x / chunksize, (int)y / chunksize, (int)z / chunksize, player, false);
                                player.CurrentChunkSentRadius = 0;
                            }, 50);

                        }

                        WatchedAttributes.SetInt("positionVersionNumber", WatchedAttributes.GetInt("positionVersionNumber", 0) + 1);

                        onTeleported?.Invoke();

                        Teleporting = false;
                    },
                });

            }
        }


        public override string GetName()
        {
            string name = GetBehavior<EntityBehaviorNameTag>()?.DisplayName;
            if (name == null) return base.GetName();
            return name;
        }

        public override string GetInfoText()
        {
            if (!Alive)
            {
                return Lang.Get(Code.Domain + ":item-dead-creature-" + Code.Path);
            }

            return Lang.Get(Code.Domain + ":item-creature-" + Code.Path);
        }

        public override void FromBytes(BinaryReader reader, bool forClient)
        {
            base.FromBytes(reader, forClient);

            lastRunningHeldUseAnimation = WatchedAttributes.GetString("lrHeldUseAnim");
            lastRunningHeldHitAnimation = WatchedAttributes.GetString("lrHeldHitAnim");
            lastRunningRightHeldIdleAnimation = WatchedAttributes.GetString("lrRightHeldIdleAnim");
        }

        public override void ToBytes(BinaryWriter writer, bool forClient)
        {
            if (lastRunningHeldUseAnimation != null)
            {
                WatchedAttributes.SetString("lrHeldUseAnim", lastRunningHeldUseAnimation);
            }
            if (lastRunningHeldHitAnimation != null)
            {
                WatchedAttributes.SetString("lrHeldHitAnim", lastRunningHeldHitAnimation);
            }
            if (lastRunningRightHeldIdleAnimation != null)
            {
                WatchedAttributes.SetString("lrRightHeldIdleAnim", lastRunningRightHeldIdleAnimation);
            }


            base.ToBytes(writer, forClient);
        }
    }
}
