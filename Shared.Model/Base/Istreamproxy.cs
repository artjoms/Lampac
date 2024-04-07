﻿namespace Shared.Model.Base
{
    public interface Istreamproxy
    {
        public bool rhub { get; set; }

        public bool useproxystream { get; set; }

        public bool streamproxy { get; set; }

        public bool apnstream { get; set; }

        public List<string>? geostreamproxy { get; set; }

        public bool qualitys_proxy { get; set; }

        public ProxySettings? proxy { get; set; }

        public string? apn { get; set; }
    }
}
