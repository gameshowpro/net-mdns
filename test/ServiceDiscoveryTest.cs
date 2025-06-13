﻿using System;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Makaretu.Dns
{

    [TestClass]
    public class ServiceDiscoveryTest
    {
        [TestMethod]
        public void Disposable()
        {
            using (var sd = new ServiceDiscovery())
            {
                Assert.IsNotNull(sd);
            }

            var mdns = new MulticastService();
            using (var sd = new ServiceDiscovery(mdns))
            {
                Assert.IsNotNull(sd);
            }
        }

        [TestMethod]
        public void Advertises_Service()
        {
            var service = new ServiceProfile("x", "_sdtest-1._udp", 1024, new[] { IPAddress.Loopback });
            var done = new ManualResetEvent(false);

            var mdns = new MulticastService();
            mdns.NetworkInterfaceDiscovered += (s, e) =>
                mdns.SendQuery(ServiceDiscovery.ServiceName, DnsClass.IN, DnsType.PTR);
            mdns.AnswerReceived += (s, e) =>
            {
                var msg = e.Message;
                if (msg.Answers.OfType<PTRRecord>().Any(p => p.DomainName == service.QualifiedServiceName && ((int)p.Class & MulticastService.CACHE_FLUSH_BIT) != 0))
                {
                    Assert.Fail("shared PTR records should not have cache-flush set");
                }
                if (msg.Answers.OfType<PTRRecord>().Any(p => p.DomainName == service.QualifiedServiceName))
                {
                    done.Set();
                }
            };
            try
            {
                using var sd = new ServiceDiscovery(mdns);
                    sd.Advertise(service);
                mdns.Start();
                Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(1)), "query timeout");
            }
            finally
            {
                mdns.Stop();
            }
        }

        [TestMethod]
        public void Advertises_SharedService()
        {
            var service = new ServiceProfile("x", "_sdtest-1._udp", 1024, new[] { IPAddress.Loopback }, true);
            var done = new ManualResetEvent(false);
            Assert.IsTrue(service.SharedProfile, "Shared Profile was not set");
            var mdns = new MulticastService();
            mdns.NetworkInterfaceDiscovered += (s, e) =>
                mdns.SendQuery(service.QualifiedServiceName, DnsClass.IN, DnsType.ANY);
            mdns.AnswerReceived += (s, e) =>
            {
                var msg = e.Message;
                if (msg.Answers.OfType<PTRRecord>().Any(p => p.DomainName == service.QualifiedServiceName && ((int)p.Class & MulticastService.CACHE_FLUSH_BIT) != 0))
                {
                    Assert.Fail("shared PTR records should not have cache-flush set");
                }
                if (msg.AdditionalRecords.OfType<SRVRecord>().Any(s => (s.Name == service.FullyQualifiedName && ((int)s.Class & MulticastService.CACHE_FLUSH_BIT) == 0)))
                {
                    done.Set();
                }
            };
            try
            {
                using var sd = new ServiceDiscovery(mdns);
                sd.Advertise(service);
                mdns.Start();
                Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(1)), "query timeout");
            }
            finally
            {
                mdns.Stop();
            }
        }

        [TestMethod]
        public void Probe_Service()
        {
            MulticastService.IncludeLoopbackInterfaces = true;
            var service = new ServiceProfile("z", "_sdtest-111._udp", 1024, new[] { IPAddress.Loopback });
            var done = new ManualResetEvent(false);

            var mdns = new MulticastService();
            using var sd = new ServiceDiscovery(mdns);
            {
                mdns.NetworkInterfaceDiscovered += (s, e) =>
                {
                    if (sd.Probe(service))
                        done.Set();
                };
                try
                {
                    sd.Advertise(service);
                    mdns.Start();
                    Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(3)), "Probe timeout");
                }
                finally
                {
                    mdns.Stop();
                    MulticastService.IncludeLoopbackInterfaces = false;
                }
            }
        }

        [TestMethod]
        public void Probe_Conflict()
        {
            var service = new ServiceProfile("z", "_sdtest-112._udp", 1024, new[] { IPAddress.Loopback });

            var sd = new ServiceDiscovery();
            sd.Advertise(service);
            sd.Mdns.Start();


            var mdns = new MulticastService();
            using var sd2 = new ServiceDiscovery(mdns);
            {
                mdns.NetworkInterfaceDiscovered += (s, e) =>
                {
                    Assert.IsTrue(sd2.Probe(service));
                };
                try
                {
                    mdns.Start();
                }
                finally
                {
                    mdns.Stop();
                }
            }
            sd.Dispose();
        }

        [TestMethod]
        public void Probe_NoConflict()
        {
            var service = new ServiceProfile("z", "_sdtest-113._udp", 1024, new[] { IPAddress.Loopback });

            var mdns = new MulticastService();
            using var sd = new ServiceDiscovery(mdns);
            {
                mdns.NetworkInterfaceDiscovered += (s, e) =>
                {
                    Assert.IsFalse(sd.Probe(service));
                };
                try
                {
                    mdns.Start();
                }
                finally
                {
                    mdns.Stop();
                }
            }
            sd.Dispose();
        }

        [TestMethod]
        public void Advertises_ServiceInstances()
        {
            var service = new ServiceProfile("x", "_sdtest-1._udp", 1024, new[] { IPAddress.Loopback });
            var done = new ManualResetEvent(false);

            var mdns = new MulticastService();
            mdns.NetworkInterfaceDiscovered += (s, e) =>
                mdns.SendQuery(service.QualifiedServiceName, DnsClass.IN, DnsType.PTR);
            mdns.AnswerReceived += (s, e) =>
            {
                var msg = e.Message;
                if (msg.Answers.OfType<PTRRecord>().Any(p => p.DomainName == service.FullyQualifiedName))
                {
                    done.Set();
                }
            };
            try
            {
                using var sd = new ServiceDiscovery(mdns);
                    sd.Advertise(service);
                mdns.Start();
                Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(1)), "query timeout");
            }
            finally
            {
                mdns.Stop();
            }
        }

        [TestMethod]
        public void Advertises_ServiceInstance_Address()
        {
            var service = new ServiceProfile("x2", "_sdtest-1._udp", 1024, new[] { IPAddress.Loopback });
            var done = new ManualResetEvent(false);

            var mdns = new MulticastService();
            mdns.NetworkInterfaceDiscovered += (s, e) =>
                mdns.SendQuery(service.HostName, DnsClass.IN, DnsType.A);
            mdns.AnswerReceived += (s, e) =>
            {
                var msg = e.Message;
                if (msg.Answers.OfType<ARecord>().Any(p => p.Name == service.HostName))
                {
                    done.Set();
                }
            };
            try
            {
                using var sd = new ServiceDiscovery(mdns);
                    sd.Advertise(service);
                mdns.Start();
                Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(1)), "query timeout");
            }
            finally
            {
                mdns.Stop();
            }
        }

        [TestMethod]
        public void Advertises_ServiceInstance_Subtype()
        {
            var service = new ServiceProfile("x2", "_sdtest-1._udp", 1024, new[] { IPAddress.Loopback });
            service.Subtypes.Add("_example");
            var done = new ManualResetEvent(false);

            var mdns = new MulticastService();
            mdns.NetworkInterfaceDiscovered += (s, e) =>
                mdns.SendQuery("_example._sub._sdtest-1._udp.local", DnsClass.IN, DnsType.PTR);
            mdns.AnswerReceived += (s, e) =>
            {
                var msg = e.Message;
                if (msg.Answers.OfType<PTRRecord>().Any(p => p.DomainName == service.FullyQualifiedName))
                {
                    done.Set();
                }
            };
            try
            {
                using var sd = new ServiceDiscovery(mdns);
                    sd.Advertise(service);
                mdns.Start();
                Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(1)), "query timeout");
            }
            finally
            {
                mdns.Stop();
            }
        }

        [TestMethod]
        public void Discover_AllServices()
        {
            var service = new ServiceProfile("x", "_sdtest-2._udp", 1024);
            var done = new ManualResetEvent(false);
            var mdns = new MulticastService();
            var sd = new ServiceDiscovery(mdns);

            mdns.NetworkInterfaceDiscovered += (s, e) => sd.QueryAllServices();
            sd.ServiceDiscovered += (s, serviceName) =>
            {
                if (serviceName == service.QualifiedServiceName)
                {
                    done.Set();
                }
            };
            try
            {
                sd.Advertise(service);
                mdns.Start();
                Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(1)), "DNS-SD query timeout");
            }
            finally
            {
                sd.Dispose();
                mdns.Stop();
            }
        }

        [TestMethod]
        public void Discover_AllServices_Unicast()
        {
            MulticastService.IncludeLoopbackInterfaces = true;
            var service = new ServiceProfile("x", "_sdtest-5._udp", 1024);
            var done = new ManualResetEvent(false);
            var mdns = new MulticastService();
            var sd = new ServiceDiscovery(mdns);

            mdns.NetworkInterfaceDiscovered += (s, e) => sd.QueryUnicastAllServices();
            sd.ServiceDiscovered += (s, serviceName) =>
            {
                if (serviceName == service.QualifiedServiceName)
                {
                    done.Set();
                }
            };
            try
            {
                sd.Advertise(service);
                mdns.Start();
                Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(1)), "DNS-SD query timeout");
            }
            finally
            {
                sd.Dispose();
                mdns.Stop();
                MulticastService.IncludeLoopbackInterfaces = false;
            }
        }

        [TestMethod]
        public void Discover_ServiceInstance()
        {
            var service = new ServiceProfile("y", "_sdtest-2._udp", 1024);
            var done = new ManualResetEvent(false);
            var mdns = new MulticastService();
            var sd = new ServiceDiscovery(mdns);

            mdns.NetworkInterfaceDiscovered += (s, e) =>
            {
                sd.QueryServiceInstances(service.ServiceName);
            };

            sd.ServiceInstanceDiscovered += (s, e) =>
            {
                if (e.ServiceInstanceName == service.FullyQualifiedName)
                {
                    Assert.IsNotNull(e.Message);
                    done.Set();
                }
            };
            try
            {
                sd.Advertise(service);
                mdns.Start();
                Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(1)), "instance not found");
            }
            finally
            {
                sd.Dispose();
                mdns.Stop();
            }
        }

        [TestMethod]
        public void Discover_ServiceInstance_with_Subtype()
        {
            var service1 = new ServiceProfile("x", "_sdtest-2._udp", 1024);
            var service2 = new ServiceProfile("y", "_sdtest-2._udp", 1024);
            service2.Subtypes.Add("apiv2");
            var done = new ManualResetEvent(false);
            var mdns = new MulticastService();
            var sd = new ServiceDiscovery(mdns);

            mdns.NetworkInterfaceDiscovered += (s, e) =>
            {
                sd.QueryServiceInstances("_sdtest-2._udp", "apiv2");
            };

            sd.ServiceInstanceDiscovered += (s, e) =>
            {
                if (e.ServiceInstanceName == service2.FullyQualifiedName)
                {
                    Assert.IsNotNull(e.Message);
                    done.Set();
                }
            };
            try
            {
                sd.Advertise(service1);
                sd.Advertise(service2);
                mdns.Start();
                Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(1)), "instance not found");
            }
            finally
            {
                sd.Dispose();
                mdns.Stop();
            }
        }

        [TestMethod]
        public void Discover_ServiceInstance_Unicast()
        {
            var service = new ServiceProfile("y", "_sdtest-5._udp", 1024);
            var done = new ManualResetEvent(false);
            var mdns = new MulticastService();
            var sd = new ServiceDiscovery(mdns);

            mdns.NetworkInterfaceDiscovered += (s, e) =>
            {
                sd.QueryUnicastServiceInstances(service.ServiceName);
            };

            sd.ServiceInstanceDiscovered += (s, e) =>
            {
                if (e.ServiceInstanceName == service.FullyQualifiedName)
                {
                    Assert.IsNotNull(e.Message);
                    done.Set();
                }
            };
            try
            {
                sd.Advertise(service);
                mdns.Start();
                Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(1)), "instance not found");
            }
            finally
            {
                sd.Dispose();
                mdns.Stop();
            }
        }

        [TestMethod]
        public void Discover_ServiceInstance_WithAnswersContainingAdditionRecords()
        {
            var service = new ServiceProfile("y", "_sdtest-2._udp", 1024, new[] { IPAddress.Parse("127.1.1.1") });
            var done = new ManualResetEvent(false);

            using var mdns = new MulticastService();
            using var sd = new ServiceDiscovery(mdns) { AnswersContainsAdditionalRecords = true };
            Message discovered = null;

            mdns.NetworkInterfaceDiscovered += (s, e) =>
            {
                sd.QueryServiceInstances(service.ServiceName);
            };

            sd.ServiceInstanceDiscovered += (s, e) =>
            {
                if (e.ServiceInstanceName == service.FullyQualifiedName)
                {
                    Assert.IsNotNull(e.Message);
                    discovered = e.Message;
                    done.Set();
                }
            };

            sd.Advertise(service);

            mdns.Start();

            Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(3)), "instance not found");

            var additionalRecordsCount =
                1 + // SRVRecord
                1 + // TXTRecord
                1; // AddressRecord

            var answersCount = additionalRecordsCount +
                1; // PTRRecord

            Assert.AreEqual(0, discovered.AdditionalRecords.Count);
            Assert.AreEqual(answersCount, discovered.Answers.Count);
        }

        [TestMethod]
        public void Unadvertise()
        {
            var service = new ServiceProfile("z", "_sdtest-7._udp", 1024);
            var done = new ManualResetEvent(false);
            var mdns = new MulticastService();
            var sd = new ServiceDiscovery(mdns);

            mdns.NetworkInterfaceDiscovered += (s, e) => sd.QueryAllServices();
            sd.ServiceInstanceShutdown += (s, e) =>
            {
                if (e.ServiceInstanceName == service.FullyQualifiedName)
                {
                    done.Set();
                }
            };
            try
            {
                sd.Advertise(service);
                mdns.Start();
                sd.Unadvertise(service);
                Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(1)), "goodbye timeout");
            }
            finally
            {
                sd.Dispose();
                mdns.Stop();
            }
        }
        [TestMethod]
        public void ReverseAddressMapping()
        {
            var service = new ServiceProfile("x9", "_sdtest-1._udp", 1024, new[] { IPAddress.Loopback, IPAddress.IPv6Loopback });
            var arpaAddress = IPAddress.Loopback.GetArpaName();
            var done = new ManualResetEvent(false);

            var mdns = new MulticastService();
            Message response = null;
            mdns.NetworkInterfaceDiscovered += (s, e) =>
                mdns.SendQuery(arpaAddress, DnsClass.IN, DnsType.PTR);
            mdns.AnswerReceived += (s, e) =>
            {
                var msg = e.Message;
                if (msg.Answers.OfType<PTRRecord>().Any(p => p.Name == arpaAddress))
                {
                    response = msg;
                    done.Set();
                }
            };
            try
            {
                using var sd = new ServiceDiscovery(mdns);
                    sd.Advertise(service);
                mdns.Start();
                Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(1)), "query timeout");
                var answers = response.Answers
                    .OfType<PTRRecord>()
                    .Where(ptr => service.HostName == ptr.DomainName);
                foreach (var answer in answers)
                {
                    Assert.AreEqual(arpaAddress, answer.Name);
                    Assert.IsTrue(answer.TTL > TimeSpan.Zero);
                    Assert.AreEqual(DnsClass.IN, answer.Class);
                }
            }
            finally
            {
                mdns.Stop();
            }
        }

        [TestMethod]
        public void ResourceRecords()
        {
            var profile = new ServiceProfile("me", "_myservice._udp", 1234, new IPAddress[] { IPAddress.Loopback });
            profile.Subtypes.Add("apiv2");
            profile.AddProperty("someprop", "somevalue");

            using var sd = new ServiceDiscovery();
                sd.Advertise(profile);

            var resourceRecords = sd.NameServer.Catalog.Values.SelectMany(node => node.Resources);
            foreach (var r in resourceRecords)
            {
                Console.WriteLine(r.ToString());
            }
        }

        [TestMethod]
        public void Announce_ContainsSharedRecords()
        {
            var service = new ServiceProfile("z", "_sdtest-4._udp", 1024, new[] { IPAddress.Loopback });
            var done = new ManualResetEvent(false);

            var mdns = new MulticastService();
            mdns.AnswerReceived += (s, e) =>
            {
                var msg = e.Message;
                if (msg.Answers.OfType<PTRRecord>().Any(p => p.DomainName == service.FullyQualifiedName))
                {
                    done.Set();
                }
            };
            try
            {
                using var sd = new ServiceDiscovery(mdns);
                    mdns.NetworkInterfaceDiscovered += (s, e) =>
                    {
                        Assert.IsFalse(sd.Probe(service));
                        sd.Announce(service);
                    };
                mdns.Start();
                Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(3)), "announce timeout");
            }
            finally
            {
                mdns.Stop();
            }
        }

        [TestMethod]
        public void Announce_ContainsResourceRecords()
        {
            var service = new ServiceProfile("z", "_sdtest-4._udp", 1024, new[] { IPAddress.Loopback });
            var done = new ManualResetEvent(false);

            var mdns = new MulticastService();
            mdns.AnswerReceived += (s, e) =>
            {
                var msg = e.Message;
                //Remove Cache-Flush bit
                foreach (var answer in e.Message.Answers)
                    answer.Class = (DnsClass)((ushort)answer.Class & ~MulticastService.CACHE_FLUSH_BIT);
                foreach (var r in service.Resources)
                {
                    if (!msg.Answers.Contains(r))
                    {
                        return;
                    }
                }
                done.Set();
            };
            try
            {
                using var sd = new ServiceDiscovery(mdns);
                    mdns.NetworkInterfaceDiscovered +=
                    (s, e) =>
                    {
                        Assert.IsFalse(sd.Probe(service));
                        sd.Announce(service);
                    };
                mdns.Start();
                Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(3)), "announce timeout");
            }
            finally
            {
                mdns.Stop();
            }
        }

        [TestMethod]
        public void Announce_SentThrice()
        {
            var service = new ServiceProfile("z", "_sdtest-4._udp", 1024, new[] { IPAddress.Loopback });
            var done = new ManualResetEvent(false);
            var nanswers = 0;
            DateTime start = DateTime.Now;
            var mdns = new MulticastService
            {
                IgnoreDuplicateMessages = false
            };
            mdns.AnswerReceived += (s, e) =>
            {
                var msg = e.Message;
                if (msg.Answers.OfType<PTRRecord>().Any(p => p.DomainName == service.FullyQualifiedName))
                {
                    if (++nanswers == 3)
                    {
                        done.Set();
                    }
                }
            };
            try
            {
                using var sd = new ServiceDiscovery(mdns);
                    mdns.NetworkInterfaceDiscovered += (s, e) =>
                    {
                        Assert.IsFalse(sd.Probe(service));
                        start = DateTime.Now;
                        sd.Announce(service, 3);
                    };
                mdns.Start();
                Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(4)), "announce timeout");
                if ((DateTime.Now - start).TotalMilliseconds < 2500)
                    Assert.Fail("Announcing too fast");
            }
            finally
            {
                mdns.Stop();
            }
        }
    }
}
