using DevExpress.ExpressApp;
using X.Sequence;

namespace X {
    public sealed partial class XModule : ModuleBase {
        public XModule() {
            InitializeComponent();
        }

        public override void Setup(XafApplication application) {
            base.Setup(application);
            SequenceGeneratorInitializer.Register(application);
        }
    }
}
