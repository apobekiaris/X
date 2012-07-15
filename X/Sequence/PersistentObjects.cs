using System.Collections.Generic;
using System.ComponentModel;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;

namespace X.Sequence {
    //Dennis: Uncomment this code if you want to have the SequentialNumber column created in each derived class table.
    [NonPersistent]
    public abstract class SequenceBaseObject : BaseObject, ISupportSequentialNumber {
        static SequenceGenerator sequenceGenerator;
        static readonly object syncRoot = new object();
        long _id;

        protected SequenceBaseObject(Session session)
            : base(session) {
        }
        #region ISupportSequentialNumber Members
        [Browsable(false)]
        //Dennis: Comment out this code if you do not want to have the SequentialNumber column created in each derived class table.
        [Indexed(Unique = false)]
        public long Id {
            get { return _id; }
            set { SetPropertyValue("Id", ref _id, value); }
        }
        #endregion
        protected override void OnSaving() {
            try {
                base.OnSaving();
                if (Session.IsNewObject(this) && !(Session is NestedUnitOfWork))
                    GenerateSequence();
            } catch {
                CancelSequence();
                throw;
            }
        }

        public void GenerateSequence() {
            lock (syncRoot) {
                var typeToExistsMap = new Dictionary<string, bool>();
                foreach (object item in Session.GetObjectsToSave())
                    typeToExistsMap[Session.GetClassInfo(item).FullName] = true;
                if (sequenceGenerator == null)
                    sequenceGenerator = new SequenceGenerator(typeToExistsMap);
                SubscribeToEvents();
                OnSequenceGenerated(sequenceGenerator.GetNextSequence(ClassInfo));
            }
        }

        void AcceptSequence() {
            lock (syncRoot) {
                if (sequenceGenerator != null) {
                    try {
                        sequenceGenerator.Accept();
                    } finally {
                        CancelSequence();
                    }
                }
            }
        }

        void CancelSequence() {
            lock (syncRoot) {
                UnSubscribeFromEvents();
                if (sequenceGenerator != null) {
                    sequenceGenerator.Close();
                    sequenceGenerator = null;
                }
            }
        }

        void Session_AfterCommitTransaction(object sender, SessionManipulationEventArgs e) {
            AcceptSequence();
        }

        void Session_AfterRollBack(object sender, SessionManipulationEventArgs e) {
            CancelSequence();
        }

        void Session_FailedCommitTransaction(object sender, SessionOperationFailEventArgs e) {
            CancelSequence();
        }

        void SubscribeToEvents() {
            if (!(Session is NestedUnitOfWork)) {
                Session.AfterCommitTransaction += Session_AfterCommitTransaction;
                Session.AfterRollbackTransaction += Session_AfterRollBack;
                Session.FailedCommitTransaction += Session_FailedCommitTransaction;
            }
        }

        void UnSubscribeFromEvents() {
            if (!(Session is NestedUnitOfWork)) {
                Session.AfterCommitTransaction -= Session_AfterCommitTransaction;
                Session.AfterRollbackTransaction -= Session_AfterRollBack;
                Session.FailedCommitTransaction -= Session_FailedCommitTransaction;
            }
        }

        void OnSequenceGenerated(long newId) {
            ((ISupportSequentialNumber)this).SequentialNumber = newId;
            Id = newId;
        }

        long ISupportSequentialNumber.SequentialNumber { get; set; }
    }
}