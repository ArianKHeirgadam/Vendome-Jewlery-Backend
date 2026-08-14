import { PackagePlus, Pencil, Trash2 } from "lucide-react";
import { type FormEvent, useMemo, useState } from "react";
import { useAuthentication } from "../auth/AuthContext";
import { useOperations } from "./OperationsContext";
import {
  EmptyState,
  FormActions,
  FormField,
  InlineError,
  Modal,
  StatusBadge,
  TableCard,
} from "./PagePrimitives";
import type { ProductCategory } from "./operations.types";

interface ProductCategoryManagerProps {
  onNotice: (message: string) => void;
}

interface CategoryFormState {
  saving: boolean;
  error: string | null;
}

const initialFormState: CategoryFormState = { saving: false, error: null };

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : "عملیات دسته‌بندی کامل نشد.";
}

function formText(form: FormData, name: string): string {
  return String(form.get(name) || "").trim();
}

export function ProductCategoryManager({ onNotice }: ProductCategoryManagerProps) {
  const { data, request, refresh } = useOperations();
  const { user } = useAuthentication();
  const canManage = user?.permissions.includes("Products.Manage") === true;
  const [editorOpen, setEditorOpen] = useState(false);
  const [editingCategory, setEditingCategory] = useState<ProductCategory | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<ProductCategory | null>(null);
  const [formState, setFormState] = useState<CategoryFormState>(initialFormState);

  const categories = useMemo(
    () => [...data.categories].sort((left, right) =>
      left.displayOrder - right.displayOrder || left.name.localeCompare(right.name, "fa")),
    [data.categories],
  );

  const productCount = (categoryId: string) =>
    data.products.filter((product) => product.productCategoryId === categoryId).length;

  const childCount = (categoryId: string) =>
    data.categories.filter((category) => category.parentCategoryId === categoryId).length;

  const parentName = (category: ProductCategory) =>
    data.categories.find((item) => item.id === category.parentCategoryId)?.name || "—";

  const openCreate = () => {
    setEditingCategory(null);
    setFormState(initialFormState);
    setEditorOpen(true);
  };

  const openEdit = (category: ProductCategory) => {
    setEditingCategory(category);
    setFormState(initialFormState);
    setEditorOpen(true);
  };

  const closeEditor = () => {
    if (formState.saving) return;
    setEditorOpen(false);
    setEditingCategory(null);
    setFormState(initialFormState);
  };

  const submitCategory = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!canManage) return;

    const form = new FormData(event.currentTarget);
    const name = formText(form, "name");
    const slug = formText(form, "slug").toLocaleLowerCase("en-US");
    const parentCategoryId = formText(form, "parentCategoryId") || null;
    const displayOrder = Number(form.get("displayOrder") || 0);

    if (!name) {
      setFormState({ saving: false, error: "نام دسته‌بندی الزامی است." });
      return;
    }
    if (!slug) {
      setFormState({ saving: false, error: "شناسه لاتین دسته‌بندی الزامی است." });
      return;
    }
    if (!Number.isInteger(displayOrder) || displayOrder < 0) {
      setFormState({ saving: false, error: "ترتیب نمایش باید یک عدد صحیح صفر یا بزرگ‌تر باشد." });
      return;
    }
    if (editingCategory && parentCategoryId === editingCategory.id) {
      setFormState({ saving: false, error: "یک دسته‌بندی نمی‌تواند والد خودش باشد." });
      return;
    }

    setFormState({ saving: true, error: null });
    try {
      const editing = editingCategory !== null;
      const payload = editing
        ? {
            name,
            slug,
            parentCategoryId,
            displayOrder,
            isActive: form.get("isActive") === "on",
            rowVersion: editingCategory.rowVersion,
          }
        : {
            name,
            slug,
            parentCategoryId,
            displayOrder,
          };

      await request<ProductCategory>(
        editing
          ? `/api/v1/catalog/categories/${editingCategory.id}`
          : "/api/v1/catalog/categories",
        {
          method: editing ? "PUT" : "POST",
          body: JSON.stringify(payload),
        },
      );

      await refresh();
      setEditorOpen(false);
      setEditingCategory(null);
      setFormState(initialFormState);
      onNotice(editing ? "دسته‌بندی محصول ویرایش شد." : "دسته‌بندی محصول ثبت شد.");
    } catch (error) {
      setFormState({ saving: false, error: errorMessage(error) });
    }
  };

  const deleteCategory = async () => {
    if (!deleteTarget || !canManage) return;

    setFormState({ saving: true, error: null });
    try {
      await request<void>(`/api/v1/catalog/categories/${deleteTarget.id}`, {
        method: "DELETE",
      });
      await refresh();
      setDeleteTarget(null);
      setFormState(initialFormState);
      onNotice("دسته‌بندی محصول حذف شد.");
    } catch (error) {
      setFormState({ saving: false, error: errorMessage(error) });
    }
  };

  return (
    <section className="module-subsection" aria-labelledby="product-category-manager-title">
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          flexWrap: "wrap",
          gap: 10,
        }}
      >
        <div>
          <h2 className="section-title" id="product-category-manager-title">دسته‌بندی محصولات</h2>
          <p className="muted-text" style={{ margin: "3px 0 0", fontSize: 10 }}>
            ساختار دسته‌بندی کاتالوگ را بساز، ویرایش کن و وضعیت آن را مدیریت کن.
          </p>
        </div>
        {canManage && (
          <button className="primary-button" type="button" onClick={openCreate}>
            <PackagePlus size={16} /> دسته‌بندی جدید
          </button>
        )}
      </div>

      {categories.length ? (
        <TableCard>
          <div className="table-scroll">
            <table className="data-table">
              <thead>
                <tr>
                  <th>نام</th>
                  <th>شناسه</th>
                  <th>دسته والد</th>
                  <th>ترتیب</th>
                  <th>محصول</th>
                  <th>زیرمجموعه</th>
                  <th>وضعیت</th>
                  <th>عملیات</th>
                </tr>
              </thead>
              <tbody>
                {categories.map((category) => (
                  <tr key={category.id}>
                    <td><strong>{category.name}</strong></td>
                    <td className="numeric-cell">{category.slug}</td>
                    <td>{parentName(category)}</td>
                    <td>{category.displayOrder}</td>
                    <td>{productCount(category.id)}</td>
                    <td>{childCount(category.id)}</td>
                    <td><StatusBadge status={category.isActive ? "Active" : "Inactive"} /></td>
                    <td>
                      {canManage ? (
                        <div className="icon-action-group">
                          <button
                            className="icon-action icon-action--gold"
                            type="button"
                            title="ویرایش دسته‌بندی"
                            aria-label={`ویرایش ${category.name}`}
                            onClick={() => openEdit(category)}
                          >
                            <Pencil size={15} />
                          </button>
                          <button
                            className="icon-action"
                            type="button"
                            title="حذف دسته‌بندی"
                            aria-label={`حذف ${category.name}`}
                            onClick={() => {
                              setFormState(initialFormState);
                              setDeleteTarget(category);
                            }}
                          >
                            <Trash2 size={15} />
                          </button>
                        </div>
                      ) : "—"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </TableCard>
      ) : (
        <EmptyState
          title="دسته‌بندی محصولی ثبت نشده"
          description="برای مرتب‌سازی محصولات و کلکسیون‌ها، اولین دسته‌بندی را ایجاد کن."
        />
      )}

      <Modal
        open={editorOpen}
        title={editingCategory ? `ویرایش دسته‌بندی ${editingCategory.name}` : "دسته‌بندی جدید"}
        description="نام، شناسه لاتین، دسته والد و ترتیب نمایش را مشخص کن."
        onClose={closeEditor}
      >
        <form className="entity-form" onSubmit={submitCategory}>
          <FormField label="نام دسته‌بندی">
            <input
              name="name"
              defaultValue={editingCategory?.name || ""}
              required
              maxLength={200}
              autoFocus
            />
          </FormField>
          <FormField label="شناسه لاتین" hint="مثلاً necklaces یا gold-rings">
            <input
              name="slug"
              dir="ltr"
              defaultValue={editingCategory?.slug || ""}
              required
              maxLength={200}
              pattern="[a-z0-9]+(?:-[a-z0-9]+)*"
            />
          </FormField>
          <FormField label="دسته والد">
            <select name="parentCategoryId" defaultValue={editingCategory?.parentCategoryId || ""}>
              <option value="">بدون دسته والد</option>
              {categories
                .filter((category) => category.id !== editingCategory?.id)
                .map((category) => (
                  <option value={category.id} key={category.id}>{category.name}</option>
                ))}
            </select>
          </FormField>
          <FormField label="ترتیب نمایش">
            <input
              name="displayOrder"
              type="number"
              min="0"
              step="1"
              defaultValue={editingCategory?.displayOrder ?? 0}
              required
            />
          </FormField>
          {editingCategory && (
            <label className="check-field">
              <input name="isActive" type="checkbox" defaultChecked={editingCategory.isActive} />
              <span>دسته‌بندی فعال باشد</span>
            </label>
          )}
          <InlineError message={formState.error} />
          <FormActions
            saving={formState.saving}
            submitLabel={editingCategory ? "ذخیره تغییرات" : "ثبت دسته‌بندی"}
            onCancel={closeEditor}
          />
        </form>
      </Modal>

      <Modal
        open={Boolean(deleteTarget)}
        title="حذف دسته‌بندی"
        description="اگر دسته‌بندی به محصول یا زیرمجموعه‌ای متصل باشد، بک‌اند از حذف ناسازگار جلوگیری می‌کند."
        onClose={() => {
          if (formState.saving) return;
          setDeleteTarget(null);
          setFormState(initialFormState);
        }}
      >
        {deleteTarget && (
          <div>
            <p style={{ marginTop: 0 }}>
              دسته‌بندی <strong>{deleteTarget.name}</strong> حذف شود؟
            </p>
            <InlineError message={formState.error} />
            <div className="form-actions">
              <button
                className="secondary-button"
                type="button"
                disabled={formState.saving}
                onClick={() => {
                  setDeleteTarget(null);
                  setFormState(initialFormState);
                }}
              >
                انصراف
              </button>
              <button
                className="primary-button"
                type="button"
                disabled={formState.saving}
                onClick={() => void deleteCategory()}
              >
                <Trash2 size={15} /> {formState.saving ? "در حال حذف…" : "حذف دسته‌بندی"}
              </button>
            </div>
          </div>
        )}
      </Modal>
    </section>
  );
}
