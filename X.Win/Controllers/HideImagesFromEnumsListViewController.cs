using System;
using System.Collections.Generic;
using System.ComponentModel;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using DevExpress.ExpressApp.Win.Editors;
using System.Linq;
using DevExpress.Utils;
using DevExpress.XtraEditors.Repository;

namespace X.Win.Controllers {
    public interface IModelColumnGlyphAligment : IModelColumn {
        [Category("X")]
        [Description("Usage:Hide the text of enums when they have images")]
        [ModelBrowsable(typeof(IModelImageComboHorzAligmentVisibilityCalculator))]
        HorzAlignment? ImageComboHorzAlignment { get; set; }
    }
    public class HideImagesFromEnumsListViewController : ViewController<ListView>, IModelExtender {
        protected override void OnViewControlsCreated() {
            base.OnViewControlsCreated();
            var gridListEditor = View.Editor as GridListEditor;
            if (gridListEditor != null) {
                foreach (var imageComboBox in GetImageComboBoxes(gridListEditor)) {
                    imageComboBox.GlyphAlignment = HorzAlignment.Center;
                }
            }
        }

        IEnumerable<RepositoryItemImageComboBox> GetImageComboBoxes(GridListEditor gridListEditor) {
            return GetEnumColumnsWithImages().Select(modelColumn => gridListEditor.GridView.Columns[modelColumn.PropertyName].ColumnEdit).OfType<RepositoryItemImageComboBox>();
        }

        IEnumerable<IModelColumn> GetEnumColumnsWithImages() {
            return View.Model.Columns.OfType<IModelColumnGlyphAligment>().Where(column => column.ImageComboHorzAlignment != null && typeof(Enum).IsAssignableFrom(column.ModelMember.Type));
        }

        void IModelExtender.ExtendModelInterfaces(ModelInterfaceExtenders extenders) {
            extenders.Add<IModelColumn, IModelColumnGlyphAligment>();
        }
    }

    public class IModelImageComboHorzAligmentVisibilityCalculator : IModelIsVisible {
        public bool IsVisible(IModelNode node, string propertyName) {
            var modelColumnGlyphAligment = node as IModelColumnGlyphAligment;
            return modelColumnGlyphAligment == null || typeof(Enum).IsAssignableFrom(modelColumnGlyphAligment.ModelMember.Type);
        }
    }
}
