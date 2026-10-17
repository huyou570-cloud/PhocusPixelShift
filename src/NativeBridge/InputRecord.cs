using System.Runtime.Serialization;

[DataContract]
public class InputRecord
{
	[DataMember]
	public string Path;

	[DataMember]
	public string SHA256;

	[DataMember]
	public long Bytes;

	[DataMember]
	public int Width;

	[DataMember]
	public int Height;

	[DataMember]
	public int SessionCount;

	[DataMember]
	public int SessionIndex;
}
