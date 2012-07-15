using System;
using System.Collections.Generic;
using System.Threading;
using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Utils;
using DevExpress.ExpressApp.Xpo;
using DevExpress.Xpo;
using DevExpress.Xpo.DB;
using DevExpress.Xpo.DB.Exceptions;
using DevExpress.Xpo.Metadata;

namespace X.Sequence {
    //Dennis: This class is used to generate sequential numbers for persistent objects.
    //Use the GetNextSequence method to get the next number and the Accept method, to save these changes to the database.
    public class SequenceGenerator : IDisposable {
        public const int MaxGenerationAttemptsCount = 10;
        public const int MinGenerationAttemptsDelay = 100;
        static volatile IDataLayer defaultDataLayer;
        static readonly object syncRoot = new Object();
        ExplicitUnitOfWork euow;
        Sequence _seq;

        public SequenceGenerator(Dictionary<string, bool> lockedSequenceTypes) {
            int count = MaxGenerationAttemptsCount;
            while (true) {
                try {
                    euow = new ExplicitUnitOfWork(DefaultDataLayer);
                    //Dennis: It is necessary to update all sequences because objects graphs may be complex enough, and so their sequences should be locked to avoid a deadlock.
                    var sequences = new XPCollection<Sequence>(euow,
                                                               new InOperator("TypeName", lockedSequenceTypes.Keys),
                                                               new SortProperty("TypeName", SortingDirection.Ascending));
                    foreach (Sequence seq in sequences)
                        seq.Save();
                    euow.FlushChanges();
                    break;
                } catch (LockingException) {
                    Close();
                    count--;
                    if (count <= 0)
                        throw;
                    Thread.Sleep(MinGenerationAttemptsDelay * count);
                }
            }
        }

        public static IDataLayer DefaultDataLayer {
            get {
                if (defaultDataLayer == null)
                    throw new ArgumentNullException("DefaultDataLayer");
                return defaultDataLayer;
            }
            set {
                lock (syncRoot)
                    defaultDataLayer = value;
            }
        }
        #region IDisposable Members
        public void Dispose() {
            Close();
        }
        #endregion
        public void Accept() {
            euow.CommitChanges();
        }

        public void Close() {
            if (euow != null) {
                if (euow.InTransaction)
                    euow.RollbackTransaction();
                euow.Dispose();
                euow = null;
            }
        }

        public long GetNextSequence(object theObject) {
            if (theObject == null)
                throw new ArgumentNullException("theObject");
            return GetNextSequence(XafTypesInfo.Instance.FindTypeInfo(theObject.GetType()));
        }

        public long GetNextSequence(ITypeInfo typeInfo) {
            if (typeInfo == null)
                throw new ArgumentNullException("typeInfo");
            return GetNextSequence(XpoTypesInfoHelper.GetXpoTypeInfoSource().XPDictionary.GetClassInfo(typeInfo.Type));
        }

        public long GetNextSequence(XPClassInfo classInfo) {
            if (classInfo == null)
                throw new ArgumentNullException("classInfo");
            XPClassInfo ci = classInfo;
            //Dennis: Uncomment this code if you want to have the SequentialNumber column created in each derived class table.
            while (ci.BaseClass != null && ci.BaseClass.IsPersistent) {
                ci = ci.BaseClass;
            }
            _seq = euow.GetObjectByKey<Sequence>(ci.FullName, true);
            if (_seq == null) {
                throw new InvalidOperationException(string.Format("Sequence for the {0} type was not found.",
                                                                  ci.FullName));
            }
            long nextSequence = _seq.NextSequence;
            _seq.NextSequence++;
            euow.FlushChanges();
            return nextSequence;
        }

        //Dennis: It is necessary to generate (only once) sequences for all the persistent types before using the GetNextSequence method.
        public static void RegisterSequences(IEnumerable<ITypeInfo> persistentTypes) {
            if (persistentTypes != null)
                using (var uow = new UnitOfWork(DefaultDataLayer)) {
                    var sequenceList = new XPCollection<Sequence>(uow);
                    var typeToExistsMap = new Dictionary<string, bool>();
                    foreach (Sequence seq in sequenceList) {
                        typeToExistsMap[seq.TypeName] = true;
                    }
                    foreach (ITypeInfo typeInfo in persistentTypes) {
                        ITypeInfo ti = typeInfo;
                        if (typeToExistsMap.ContainsKey(ti.FullName)) continue;
                        //Dennis: Uncomment this code if you want to have the SequentialNumber column created in each derived class table.
                        while (ti.Base != null && ti.Base.IsPersistent) {
                            ti = ti.Base;
                        }
                        string typeName = ti.FullName;
                        //Dennis: This code is required for the Domain Components only.
                        if (ti.IsInterface && ti.IsPersistent) {
                            Type generatedEntityType =
                                XpoTypesInfoHelper.GetXpoTypeInfoSource().GetGeneratedEntityType(ti.Type);
                            if (generatedEntityType != null)
                                typeName = generatedEntityType.FullName;
                        }
                        if (typeToExistsMap.ContainsKey(typeName)) continue;
                        if (ti.IsPersistent) {
                            typeToExistsMap[typeName] = true;
                            var seq = new Sequence(uow);
                            seq.TypeName = typeName;
                            seq.NextSequence = 0;
                        }
                    }
                    uow.CommitChanges();
                }
        }
    }

    //This persistent class is used to store last sequential number for persistent objects.
    public class Sequence : XPBaseObject {
        long nextSequence;
        string typeName;

        public Sequence(Session session)
            : base(session) {
        }

        [Key]
        //Dennis: The size should be enough to store a full type name. However, you cannot use unlimited size for key columns.
        [Size(1024)]
        public string TypeName {
            get { return typeName; }
            set { SetPropertyValue("TypeName", ref typeName, value); }
        }

        public long NextSequence {
            get { return nextSequence; }
            set { SetPropertyValue("NextSequence", ref nextSequence, value); }
        }
    }

    public interface ISupportSequentialNumber {
        long SequentialNumber { get; set; }
    }

    public static class SequenceGeneratorInitializer {
        static XafApplication application;

        static XafApplication Application {
            get { return application; }
        }

        public static void Register(XafApplication app) {
            application = app;
            if (application != null)
                application.LoggedOn += application_LoggedOn;
        }

        static void application_LoggedOn(object sender, LogonEventArgs e) {
            Initialize();
        }

        //Dennis: It is important to set the SequenceGenerator.DefaultDataLayer property to the main application data layer.
        //If you use a custom IObjectSpaceProvider implementation, ensure that it exposes a working IDataLayer.
        public static void Initialize() {
            Guard.ArgumentNotNull(Application, "Application");
            var provider = Application.ObjectSpaceProvider as XPObjectSpaceProvider;
            Guard.ArgumentNotNull(provider, "provider");
            if (provider.DataLayer == null) {
                //Dennis: This call is necessary to initialize a working data layer.
                provider.CreateObjectSpace();
            }
            if (provider.DataLayer is ThreadSafeDataLayer) {
                //Dennis: We have to use a separate datalayer for the sequence generator because ThreadSafeDataLayer is usually used for ASP.NET applications.
                SequenceGenerator.DefaultDataLayer =
                    XpoDefault.GetDataLayer(
                        Application.Connection == null
                            ? Application.ConnectionString
                            : Application.Connection.ConnectionString, AutoCreateOption.None);
            } else {
                SequenceGenerator.DefaultDataLayer = provider.DataLayer;
            }
        }
    }
}