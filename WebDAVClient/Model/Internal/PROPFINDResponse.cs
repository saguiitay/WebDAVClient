namespace WebDAVClient.Model.Internal
{
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "DAV:")]
    [System.Xml.Serialization.XmlRootAttribute(Namespace = "DAV:", IsNullable = false, ElementName = "multistatus")]
    public class PROPFINDResponse
    {
        
        [System.Xml.Serialization.XmlElementAttribute("response")]
        public PROPFINDItem[] Response { get; set; }
    }

}