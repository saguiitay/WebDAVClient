namespace WebDAVClient.Model.Internal
{
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "DAV:")]
    public class multistatusResponsePropstatProp
    {

        [System.Xml.Serialization.XmlElementAttribute("getcontentlength")]
        public IntProperty ContentLength { get; set; }


        [System.Xml.Serialization.XmlElementAttribute("creationdate")]
        public DateProperty CreationDate { get; set; }


        [System.Xml.Serialization.XmlElementAttribute("displayname")]
        public string DisplayName { get; set; }


        [System.Xml.Serialization.XmlElementAttribute("getetag")]
        public string Etag { get; set; }


        [System.Xml.Serialization.XmlElementAttribute("getlastmodified")]
        public StringProperty LastModified { get; set; }


        [System.Xml.Serialization.XmlElementAttribute("resourcetype")]
        public multistatusResponsePropstatPropResourcetype ResourceType { get; set; }


        [System.Xml.Serialization.XmlElementAttribute("ishidden")]
        public BooleanProperty IsHidden { get; set; }


        [System.Xml.Serialization.XmlElementAttribute("iscollection")]
        public BooleanProperty IsCollection { get; set; }


        [System.Xml.Serialization.XmlElementAttribute("getcontenttype")]
        public string ContentType { get; set; }
    }
}