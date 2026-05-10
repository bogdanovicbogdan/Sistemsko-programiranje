using System;

namespace Prvi_projekat
{
    public class Clanak
    {
        public string? Naslov { get; set; }
        public string? Abstract { get; set; }
        public string? Credit { get; set; }
        public string? WebUrl { get; set; }
        public string? DatumObjave { get; set; }

        public Clanak(string? naslov, string? abstractText, string? credit, string? webUrl, string? datumObjave)
        {
            Naslov = naslov;
            Abstract = abstractText;
            Credit = credit;
            WebUrl = webUrl;
            DatumObjave = datumObjave;
        }
    }
}