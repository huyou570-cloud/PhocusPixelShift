using System.Runtime.Serialization;

[DataContract]
public class BridgeJob
{
	[DataMember]
	public string PhocusHome;

	[DataMember]
	public string[] Inputs;

	[DataMember]
	public string Output;

	[DataMember]
	public bool OrderConfirmed;
}
