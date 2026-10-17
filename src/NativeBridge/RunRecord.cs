using System;
using System.Runtime.Serialization;

[DataContract]
public class RunRecord
{
	[DataMember]
	public string Status = "starting";

	[DataMember]
	public string StartedUtc = DateTime.UtcNow.ToString("o");

	[DataMember]
	public string EndedUtc;

	[DataMember]
	public string EngineSHA256 = "70b697d839378c58de086cb82b81ecefd2b1d723ea7361bcdaef0c8a04a6211a";

	[DataMember]
	public string OutputType = "MultiShot FFF intermediate; native TIFF rendering/export not performed";

	[DataMember]
	public string Output;

	[DataMember]
	public string OutputSHA256;

	[DataMember]
	public string Error;

	[DataMember]
	public InputRecord[] Inputs;

	[DataMember]
	public bool OriginalsVerifiedUnchanged;

	[DataMember]
	public bool RealPhotoOpticalValidation;
}
