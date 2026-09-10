using System.Xml.Serialization;

namespace SharpBastion.ClientResponseObject;

[XmlRoot("repomix")]
public class RepomixCliClientPackageRepositoryResponseObject
{
    public string TempDirectory { get; set; }
    public string XmlFileName { get; set; }

    [XmlElement("file_summary")]
    public FileSummary file_summary { get; set; }

    [XmlElement("directory_structure")]
    public string directory_structure { get; set; }

    [XmlElement("files")]
    public Files files { get; set; }
}

public class FileSummary
{
    [XmlElement("purpose")]
    public string purpose { get; set; }

    [XmlElement("file_format")]
    public string file_format { get; set; }

    [XmlElement("usage_guidelines")]
    public string usage_guidelines { get; set; }

    [XmlElement("notes")]
    public string notes { get; set; }
}

public class Files
{
    [XmlElement("file")]
    public List<RepoFile> file { get; set; } = new();
}

public class RepoFile
{
    [XmlAttribute("path")]
    public string path { get; set; }

    [XmlText]
    public string content { get; set; }
}