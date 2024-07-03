using Orchestrion.BGMSystem;

namespace Orchestrion.Game.BGMSystem;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct BGMScene
{
	public int SceneIndex;
	public SceneFlags Flags;
	private int Padding1;
	// often writing songId will cause songId2 and 3 to be written automatically
	// songId3 is sometimes not updated at all, and I'm unsure of its use
	// zeroing out songId2 seems to be necessary to actually cancel playback without using
	// an invalid id (which is the only way to do it with just songId1)
	public ushort BgmReference;       // Reference to sheet; BGM, BGMSwitch, BGMSituation
	public ushort BgmId;              // Actual BGM that's playing. Game will manage this if it's a switch or situation
	public ushort PreviousBgmId;      // BGM that was playing before this one; I think it only changed if the previous BGM 
	public byte TimerEnable;            // whether the timer automatically counts up
	private byte Padding2;
	public float Timer;                 // if enabled, seems to always count from 0 to 6
	// if 0x30 is 0, up through 0x4F are 0
	// in theory function params can be written here if 0x30 is non-zero but I've never seen it
	private fixed byte DisableRestartList[24]; // 'vector' of bgm ids that will be restarted - managed by game. it is 3 pointers
	private byte Unknown1;
	private uint Unknown2;
	private uint Unknown3;
	private uint Unknown4;
	private uint Unknown5;
	private uint Unknown6;
	private ulong Unknown7;
	private uint Unknown8;
	private byte Unknown9;
	private byte Unknown10;
	private byte Unknown11;
	private byte Unknown12;
	private float Unknown13;
	private uint Unknown14;
}