namespace WorldRpg.Rulesets.Daggerfall.Modules.Behavior;

/// <summary>Where an enemy's behaviour stands after a decision; a transition between two is a fact.</summary>
internal enum EnemyBehaviorState { Idle, Chase, Retreat, Strafe, Attack, Blocked, Search, TargetLost, Unloaded, Dead }
