namespace Miner49er.Core;

// Global prize event: a periodically-spawned objective competitive players rush and
// fight over. One event exists at a time; the type is picked at random per occurrence.
public enum PrizeType { GrabAndGo, MineOut, HoldPoint, CarryRelic }
public enum PrizeState { Idle, Telegraph, Active }
